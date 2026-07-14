using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Routier
{
  /// <summary>
  /// Modal screen-space UI listing a hub's daily route offers with a detail panel,
  /// replacing the old "click repeatedly to cycle + buy blind" kiosk flow. Built once
  /// lazily and kept hidden between uses (like the vanilla EconomyUI/MissionListUI
  /// pattern: SetActive toggling + MouseLook/Refs control locks), not baked to a
  /// texture like the parchment pages.
  /// </summary>
  internal sealed class RouteOffersUI : MonoBehaviour
  {
    private const int PanelW = 1000;
    private const int PanelH = 760;
    private const int ListX = 30;
    private const int ListW = 430;
    private const int ListStartY = 104;
    private const int RowH = 84;
    private const int RowGap = 10;
    private const int DetailX = 520;
    private const int DetailW = 450;

    private static readonly Color PanelColor = new Color(0.86f, 0.78f, 0.62f, 0.98f);
    private static readonly Color BorderColor = new Color(0.25f, 0.16f, 0.08f, 1f);
    private static readonly Color InkColor = new Color(0.08f, 0.05f, 0.03f, 1f);
    private static readonly Color RowNormal = new Color(1f, 1f, 1f, 0.16f);
    private static readonly Color RowSelected = new Color(0.98f, 0.85f, 0.35f, 0.55f);
    private static readonly Color RowLocked = new Color(0.45f, 0.45f, 0.45f, 0.22f);
    private static readonly Color RowLockedSelected = new Color(0.55f, 0.5f, 0.45f, 0.35f);
    private static readonly Color InkMuted = new Color(0.35f, 0.32f, 0.28f, 0.85f);
    private static readonly Color InkLocked = new Color(0.5f, 0.48f, 0.44f, 0.9f);

    internal static RouteOffersUI Instance;

    private GameObject _root;
    private RectTransform _panelRect;
    private Text _titleText;
    private Text _headerCurrency;
    private Text _detailTitle;
    private Text _detailRoute;
    private Text _detailStatsLabels;
    private Text _detailStatsValues;
    private Button _buyButton;
    private Text _buyButtonLabel;

    private readonly List<RowRefs> _rows = new List<RowRefs>();
    private IReadOnlyList<RouteOffer> _offers;
    private int _hubPortIndex;
    private int _playerRepLevel;
    private GenerationConfig _cfg;
    private int _selected = -1;

    private sealed class RowRefs
    {
      internal GameObject Go;
      internal Image Background;
      internal bool Locked;
    }

    internal static void EnsureInstance()
    {
      if (Instance != null)
        return;
      var go = new GameObject("RoutierOffersUI");
      Object.DontDestroyOnLoad(go);
      Instance = go.AddComponent<RouteOffersUI>();
      Instance.Build();
    }

    internal void Open(
      int hubPortIndex,
      string hubName,
      IReadOnlyList<RouteOffer> offers,
      int playerRepLevel,
      GenerationConfig cfg)
    {
      _hubPortIndex = hubPortIndex;
      _offers = offers;
      _playerRepLevel = playerRepLevel;
      _cfg = cfg;

      _titleText.text = "<b>Route Agent — " + hubName + "</b>";
      if (_offers != null && _offers.Count > 0)
      {
        var cur = PlayerGold.GetCurrencyName(_offers[0].HubRegion);
        _headerCurrency.text = RouteBoardReport.FormatHeaderLine(cur, _offers[0].GameDay);
      }
      else
        _headerCurrency.text = "";
      PopulateRows();
      if (_rows.Count > 0)
      {
        var firstUnlocked = 0;
        for (var i = 0; i < _rows.Count; i++)
        {
          if (!_rows[i].Locked)
          {
            firstUnlocked = i;
            break;
          }
        }
        Select(firstUnlocked);
      }

      _root.SetActive(true);
      MouseLook.ToggleMouseLookAndCursor(false);
      Refs.SetPlayerControl(false);
      UISoundPlayer.instance.PlayOpenSound();
    }

    private void Close()
    {
      if (_root == null || !_root.activeSelf)
        return;
      _root.SetActive(false);
      MouseLook.ToggleMouseLookAndCursor(true);
      Refs.SetPlayerControl(true);
      UISoundPlayer.instance.PlayCloseSound();
    }

    private void Update()
    {
      if (_root != null && _root.activeSelf && Input.GetKeyDown(KeyCode.Escape))
        Close();
    }

    private void PopulateRows()
    {
      foreach (var row in _rows)
        Destroy(row.Go);
      _rows.Clear();

      if (_offers == null)
        return;

      for (var i = 0; i < _offers.Count; i++)
      {
        var offer = _offers[i];
        var locked = !RouteTierTable.PlayerCanAccessOffer(_playerRepLevel, offer, _cfg);
        var y = ListStartY + i * (RowH + RowGap);

        var rowGo = new GameObject("Row" + i, typeof(RectTransform));
        rowGo.transform.SetParent(_panelRect, false);
        var rt = rowGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(ListX, -y);
        rt.sizeDelta = new Vector2(ListW, RowH);

        var bg = rowGo.AddComponent<Image>();
        bg.color = locked ? RowLocked : RowNormal;

        var button = rowGo.AddComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = locked
          ? new Color(0.55f, 0.55f, 0.55f, 0.35f)
          : new Color(1f, 1f, 1f, 0.32f);
        colors.pressedColor = locked
          ? new Color(0.6f, 0.6f, 0.6f, 0.45f)
          : new Color(1f, 1f, 1f, 0.45f);
        button.colors = colors;

        var ink = locked ? InkLocked : InkColor;
        var tierPart = locked
          ? "<color=#AA2222><b>Tier " + offer.RouteTier + "</b></color>"
          : "<b>Tier " + offer.RouteTier + "</b>";
        var titleLine = tierPart + " · " + Capitalize(offer.Tier) + " " + Capitalize(offer.Kind);
        AddText(rt, 12, 6, ListW - 24, 30, titleLine, 22, TextAnchor.UpperLeft, false, true, ink);
        AddText(rt, 12, 6, ListW - 24, 30, offer.Price.ToString(),
          22, TextAnchor.UpperRight, true, true, ink);

        var profitD = offer.DisplayProfit > 0 ? offer.DisplayProfit : offer.Profit;
        var subLine = "Profit: ~" + profitD + "  ·  " + offer.Plan.Route.Count + " stops  ·  ROI "
          + Mathf.RoundToInt(offer.Roi * 100f) + "%";
        AddText(rt, 12, 42, ListW - 24, 28, subLine, 17, TextAnchor.UpperLeft, false, true, ink);

        var idx = i;
        button.onClick.AddListener(() => Select(idx));

        _rows.Add(new RowRefs { Go = rowGo, Background = bg, Locked = locked });
      }
    }

    private void Select(int index)
    {
      if (_offers == null || index < 0 || index >= _offers.Count)
        return;
      _selected = index;
      for (var i = 0; i < _rows.Count; i++)
      {
        var row = _rows[i];
        if (i == index)
          row.Background.color = row.Locked ? RowLockedSelected : RowSelected;
        else
          row.Background.color = row.Locked ? RowLocked : RowNormal;
      }

      var offer = _offers[index];
      var locked = _rows[index].Locked;
      var reqRep = RouteTierTable.RequiredPlayerRep(offer);
      var cur = PlayerGold.GetCurrencyName(offer.HubRegion);
      var nm = Mathf.RoundToInt(offer.TotalDistanceNm);
      var capitalD = RouteDisplay.RawToDisplay(offer.CapitalInitial, offer.HubRegion);
      var profitD = offer.DisplayProfit > 0 ? offer.DisplayProfit : offer.Profit;

      _detailTitle.text = "<b>Tier " + offer.RouteTier + " · " + Capitalize(offer.Tier) + " " + Capitalize(offer.Kind) + "</b>";
      _detailRoute.text = offer.Plan?.RouteNames != null
        ? string.Join("  ->  ", offer.Plan.RouteNames)
        : "";

      _detailStatsLabels.text = "Hops:\nDistance:\nBudget:\nEst. profit:\nROI:\nPeak weight:\nPeak volume:";
      _detailStatsValues.text =
        offer.Plan.Route.Count + "\n" +
        "~" + nm + " nm\n" +
        capitalD + "\n" +
        "~" + profitD + "\n" +
        Mathf.RoundToInt(offer.Roi * 100f) + "%\n" +
        Mathf.RoundToInt(offer.Plan.WeightUsed) + " lb\n" +
        Mathf.RoundToInt(offer.Plan.VolumeUsed) + " ft³";

      if (locked)
        _buyButtonLabel.text = "<b>Rep L" + reqRep + " required</b>";
      else
        _buyButtonLabel.text = "<b>Buy — " + offer.Price + " " + cur + "</b>";
      _buyButton.interactable = !locked;
    }

    private void OnBuyClicked()
    {
      if (_offers == null || _selected < 0 || _selected >= _offers.Count)
        return;
      if (_rows[_selected].Locked)
      {
        var req = RouteTierTable.RequiredPlayerRep(_offers[_selected]);
        NotificationUi.instance.ShowNotification(RouteNotifications.NeedRepLevel(req));
        return;
      }
      var offer = _offers[_selected];

      var region = offer.HubRegion;
      if (region < 0 || region >= PlayerGold.currency.Length)
      {
        NotificationUi.instance.ShowNotification("Invalid port currency.");
        return;
      }
      if (PlayerGold.currency[region] < offer.Price)
      {
        NotificationUi.instance.ShowNotification(RouteNotifications.NotEnoughMoney());
        return;
      }

      PlayerGold.currency[region] -= offer.Price;

      if (!TryGetParchmentSpawn(_hubPortIndex, out var spawnPos, out var spawnRot))
      {
        spawnPos = Vector3.zero;
        spawnRot = Quaternion.identity;
      }

      RouteParchmentFactory.Spawn(spawnPos, spawnRot, offer.Pages);

      var profitD = offer.DisplayProfit > 0 ? offer.DisplayProfit : offer.Profit;
      NotificationUi.instance.ShowNotification(
        RouteNotifications.GuidePurchased(offer.Tier, offer.Kind, profitD));
      UISoundPlayer.instance.PlayWritingSound();
      UISoundPlayer.instance.PlayGoldSound();

      Close();
    }

    /// <summary>Spawn purchased parchment — Hack local (−0.5, 1.8, −0.7) on the kiosk.</summary>
    private static readonly Vector3 GrcParchmentSpawnLocal = new Vector3(-0.5f, 1.8f, -0.7f);

    private static bool TryGetParchmentSpawn(int hubPortIndex, out Vector3 position, out Quaternion rotation)
    {
      position = Vector3.zero;
      rotation = Quaternion.identity;

      var kiosk = FindHubKiosk(hubPortIndex);
      if (kiosk == null)
        return false;

      var yaw = MarketStallVisual.KioskYaw(kiosk);
      rotation = Quaternion.Euler(0f, yaw, 0f);
      position = kiosk.TransformPoint(GrcParchmentSpawnLocal);

      var book = kiosk.Find("RoutierRouteBook");
      if (book != null)
        rotation = book.rotation;

      return true;
    }

    private static Transform FindHubKiosk(int hubPortIndex)
    {
      if (Port.ports != null && hubPortIndex >= 0 && hubPortIndex < Port.ports.Length)
      {
        var port = Port.ports[hubPortIndex];
        if (port != null)
        {
          var underPort = port.transform.Find("RoutierRouteKiosk");
          if (underPort != null)
            return underPort;
        }
      }

      foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
      {
        if (go == null || go.name != "RoutierRouteKiosk")
          continue;
        if (!go.scene.IsValid() || !go.scene.isLoaded)
          continue;
        var agent = go.GetComponentInChildren<RouteAgentKiosk>(true);
        if (agent != null && agent.hubPortIndex == hubPortIndex)
          return go.transform;
      }

      return null;
    }

    private static string Capitalize(string s)
    {
      if (string.IsNullOrEmpty(s))
        return s;
      return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    private void Build()
    {
      EnsureEventSystem();

      _root = new GameObject("RoutierOffersRoot", typeof(RectTransform));
      _root.transform.SetParent(transform, false);
      var canvas = _root.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = 500;
      _root.AddComponent<GraphicRaycaster>();
      var scaler = _root.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920, 1080);
      scaler.matchWidthOrHeight = 0.5f;

      var dimmerGo = new GameObject("Dimmer", typeof(RectTransform));
      dimmerGo.transform.SetParent(_root.transform, false);
      var dimmerRect = dimmerGo.GetComponent<RectTransform>();
      dimmerRect.anchorMin = Vector2.zero;
      dimmerRect.anchorMax = Vector2.one;
      dimmerRect.sizeDelta = Vector2.zero;
      dimmerGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

      var borderGo = new GameObject("Border", typeof(RectTransform));
      borderGo.transform.SetParent(_root.transform, false);
      var borderRect = borderGo.GetComponent<RectTransform>();
      borderRect.anchorMin = borderRect.anchorMax = new Vector2(0.5f, 0.5f);
      borderRect.pivot = new Vector2(0.5f, 0.5f);
      borderRect.sizeDelta = new Vector2(PanelW + 12, PanelH + 12);
      borderGo.AddComponent<Image>().color = BorderColor;

      var panelGo = new GameObject("Panel", typeof(RectTransform));
      panelGo.transform.SetParent(_root.transform, false);
      _panelRect = panelGo.GetComponent<RectTransform>();
      _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
      _panelRect.pivot = new Vector2(0.5f, 0.5f);
      _panelRect.sizeDelta = new Vector2(PanelW, PanelH);
      panelGo.AddComponent<Image>().color = PanelColor;

      _titleText = AddText(_panelRect, 30, 20, PanelW - 110, 36, "", 28, TextAnchor.UpperLeft, true);
      _headerCurrency = AddText(_panelRect, 30, 54, PanelW - 110, 34, "", 15, TextAnchor.UpperLeft, false, false);
      AddHDivider(_panelRect, 30, 84, PanelW - 60);

      var (closeBtnGo, closeButton, _) = CreateButton(_panelRect, PanelW - 70, 16, 44, 44, "X", 24);
      closeButton.onClick.AddListener(Close);

      AddVDivider(_panelRect, PanelW / 2, ListStartY - 4, PanelH - ListStartY - 30);

      _detailTitle = AddText(_panelRect, DetailX, ListStartY, DetailW, 30, "", 20, TextAnchor.UpperLeft, true, false);
      AddText(_panelRect, DetailX, ListStartY + 40, DetailW, 30, "Route:", 20, TextAnchor.UpperLeft, true);
      _detailRoute = AddText(_panelRect, DetailX, ListStartY + 70, DetailW, 90, "", 20, TextAnchor.UpperLeft, false, false);

      var statsY = ListStartY + 170;
      _detailStatsLabels = AddText(_panelRect, DetailX, statsY, 195, 300, "", 22, TextAnchor.UpperLeft, false, false);
      _detailStatsValues = AddText(_panelRect, DetailX + 205, statsY, DetailW - 205, 300, "", 22, TextAnchor.UpperRight, false, false);

      var (buyGo, buyButton, buyLabel) = CreateButton(_panelRect, DetailX, PanelH - 90, DetailW, 60, "", 24);
      _buyButton = buyButton;
      _buyButtonLabel = buyLabel;
      _buyButton.onClick.AddListener(OnBuyClicked);

      _root.SetActive(false);
    }

    private static void EnsureEventSystem()
    {
      if (Object.FindObjectOfType<EventSystem>() != null)
        return;
      var go = new GameObject("RoutierEventSystem");
      go.AddComponent<EventSystem>();
      go.AddComponent<StandaloneInputModule>();
    }

    private Text AddText(
      RectTransform parent, float xPx, float yFromTopPx, float widthPx, float heightPx, string text,
      int fontSize, TextAnchor anchor, bool bold, bool singleLine = true, Color? color = null)
    {
      var go = new GameObject("Txt", typeof(RectTransform));
      go.transform.SetParent(parent, false);

      var rt = go.GetComponent<RectTransform>();
      rt.anchorMin = new Vector2(0f, 1f);
      rt.anchorMax = new Vector2(0f, 1f);
      rt.pivot = new Vector2(0f, 1f);
      rt.anchoredPosition = new Vector2(xPx, -yFromTopPx);
      rt.sizeDelta = new Vector2(widthPx, heightPx);

      var uiText = go.AddComponent<Text>();
      uiText.font = RouteParchmentPageTexture.GetFont();
      uiText.fontSize = fontSize;
      uiText.alignment = anchor;
      uiText.color = color ?? InkColor;
      uiText.supportRichText = true;
      uiText.text = bold ? "<b>" + text + "</b>" : text;
      uiText.horizontalOverflow = singleLine ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
      uiText.verticalOverflow = singleLine ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
      if (singleLine)
      {
        uiText.resizeTextForBestFit = true;
        uiText.resizeTextMinSize = 12;
        uiText.resizeTextMaxSize = fontSize;
      }
      return uiText;
    }

    private (GameObject go, Button button, Text label) CreateButton(
      RectTransform parent, float xPx, float yFromTopPx, float widthPx, float heightPx, string text, int fontSize)
    {
      var go = new GameObject("Btn", typeof(RectTransform));
      go.transform.SetParent(parent, false);

      var rt = go.GetComponent<RectTransform>();
      rt.anchorMin = new Vector2(0f, 1f);
      rt.anchorMax = new Vector2(0f, 1f);
      rt.pivot = new Vector2(0f, 1f);
      rt.anchoredPosition = new Vector2(xPx, -yFromTopPx);
      rt.sizeDelta = new Vector2(widthPx, heightPx);

      var img = go.AddComponent<Image>();
      img.color = new Color(0.35f, 0.24f, 0.12f, 0.95f);

      var button = go.AddComponent<Button>();
      var colors = button.colors;
      colors.highlightedColor = new Color(0.45f, 0.32f, 0.18f, 0.95f);
      colors.pressedColor = new Color(0.55f, 0.4f, 0.24f, 0.95f);
      button.colors = colors;

      var labelGo = new GameObject("Label", typeof(RectTransform));
      labelGo.transform.SetParent(go.transform, false);
      var labelRt = labelGo.GetComponent<RectTransform>();
      labelRt.anchorMin = Vector2.zero;
      labelRt.anchorMax = Vector2.one;
      labelRt.sizeDelta = Vector2.zero;

      var label = labelGo.AddComponent<Text>();
      label.font = RouteParchmentPageTexture.GetFont();
      label.fontSize = fontSize;
      label.alignment = TextAnchor.MiddleCenter;
      label.color = new Color(0.95f, 0.9f, 0.8f, 1f);
      label.supportRichText = true;
      label.text = text;
      label.horizontalOverflow = HorizontalWrapMode.Wrap;
      label.verticalOverflow = VerticalWrapMode.Truncate;
      label.resizeTextForBestFit = true;
      label.resizeTextMinSize = 12;
      label.resizeTextMaxSize = fontSize;

      return (go, button, label);
    }

    private static void AddHDivider(RectTransform parent, float xPx, float yFromTopPx, float widthPx)
    {
      var go = new GameObject("HDivider", typeof(RectTransform));
      go.transform.SetParent(parent, false);
      var rt = go.GetComponent<RectTransform>();
      rt.anchorMin = new Vector2(0f, 1f);
      rt.anchorMax = new Vector2(0f, 1f);
      rt.pivot = new Vector2(0f, 1f);
      rt.anchoredPosition = new Vector2(xPx, -yFromTopPx);
      rt.sizeDelta = new Vector2(Mathf.Max(2f, widthPx), 2f);
      go.AddComponent<Image>().color = new Color(InkColor.r, InkColor.g, InkColor.b, 0.55f);
    }

    private static void AddVDivider(RectTransform parent, float xPx, float yFromTopPx, float heightPx)
    {
      var go = new GameObject("VDivider", typeof(RectTransform));
      go.transform.SetParent(parent, false);
      var rt = go.GetComponent<RectTransform>();
      rt.anchorMin = new Vector2(0f, 1f);
      rt.anchorMax = new Vector2(0f, 1f);
      rt.pivot = new Vector2(0f, 1f);
      rt.anchoredPosition = new Vector2(xPx, -yFromTopPx);
      rt.sizeDelta = new Vector2(2f, Mathf.Max(2f, heightPx));
      go.AddComponent<Image>().color = new Color(InkColor.r, InkColor.g, InkColor.b, 0.55f);
    }
  }
}
