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
    private const int ListStartY = 118;
    private const int RowH = 84;
    private const int RowGap = 10;
    private const int DetailX = 520;
    private const int DetailW = 450;

    private static readonly Color PanelColor = new Color(0.86f, 0.78f, 0.62f, 0.98f);
    private static readonly Color BorderColor = new Color(0.25f, 0.16f, 0.08f, 1f);
    private static readonly Color InkColor = new Color(0.08f, 0.05f, 0.03f, 1f);
    private static readonly Color RowNormal = new Color(1f, 1f, 1f, 0.16f);
    private static readonly Color RowSelected = new Color(0.98f, 0.85f, 0.35f, 0.55f);

    internal static RouteOffersUI Instance;

    private GameObject _root;
    private RectTransform _panelRect;
    private Text _titleText;
    private Text _detailTitle;
    private Text _detailRoute;
    private Text _detailCurrency;
    private Text _detailStatsLabels;
    private Text _detailStatsValues;
    private Button _buyButton;
    private Text _buyButtonLabel;

    private readonly List<RowRefs> _rows = new List<RowRefs>();
    private IReadOnlyList<RouteOffer> _offers;
    private int _hubPortIndex;
    private int _selected = -1;

    private sealed class RowRefs
    {
      internal GameObject Go;
      internal Image Background;
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

    internal void Open(int hubPortIndex, string hubName, IReadOnlyList<RouteOffer> offers)
    {
      _hubPortIndex = hubPortIndex;
      _offers = offers;

      _titleText.text = "<b>Route Agent — " + hubName + "</b>";
      PopulateRows();
      if (_rows.Count > 0)
        Select(0);

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
        bg.color = RowNormal;

        var button = rowGo.AddComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.32f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.45f);
        button.colors = colors;

        var cur = PlayerGold.GetCurrencyName(offer.HubRegion);
        AddText(rt, 12, 6, ListW - 24, 30, Capitalize(offer.Tier) + " " + Capitalize(offer.Kind),
          22, TextAnchor.UpperLeft, true);
        AddText(rt, 12, 6, ListW - 24, 30, offer.Price + " " + cur,
          22, TextAnchor.UpperRight, true);

        var profitD = offer.DisplayProfit > 0 ? offer.DisplayProfit : offer.Profit;
        AddText(rt, 12, 42, ListW - 24, 28,
          "~" + profitD + " " + cur + " profit  ·  " + offer.Plan.Route.Count + " stops  ·  ROI "
          + Mathf.RoundToInt(offer.Roi * 100f) + "%",
          17, TextAnchor.UpperLeft, false);

        var idx = i;
        button.onClick.AddListener(() => Select(idx));

        _rows.Add(new RowRefs { Go = rowGo, Background = bg });
      }
    }

    private void Select(int index)
    {
      if (_offers == null || index < 0 || index >= _offers.Count)
        return;
      _selected = index;
      for (var i = 0; i < _rows.Count; i++)
        _rows[i].Background.color = i == index ? RowSelected : RowNormal;

      var offer = _offers[index];
      var cur = PlayerGold.GetCurrencyName(offer.HubRegion);
      var miles = Mathf.RoundToInt(RouteParchmentBuilder.DistanceMiles(offer.TotalDistanceKm));
      var capitalD = RouteDisplay.RawToDisplay(offer.CapitalInitial, offer.HubRegion);
      var profitD = offer.DisplayProfit > 0 ? offer.DisplayProfit : offer.Profit;

      _detailTitle.text = "<b>" + Capitalize(offer.Tier) + " " + Capitalize(offer.Kind) + " route</b>";
      _detailRoute.text = offer.Plan?.RouteNames != null
        ? string.Join("  ->  ", offer.Plan.RouteNames)
        : "";
      _detailCurrency.text = cur;

      _detailStatsLabels.text = "Hops:\nDistance:\nBudget:\nEst. profit:\nROI:\nPeak weight:\nPeak volume:";
      _detailStatsValues.text =
        offer.Plan.Route.Count + "\n" +
        "~" + miles + " mi\n" +
        capitalD + "\n" +
        "~" + profitD + "\n" +
        Mathf.RoundToInt(offer.Roi * 100f) + "%\n" +
        Mathf.RoundToInt(offer.Plan.WeightUsed) + " lb\n" +
        Mathf.RoundToInt(offer.Plan.VolumeUsed) + " ft³";

      _buyButtonLabel.text = "<b>Buy — " + offer.Price + " " + cur + "</b>";
    }

    private void OnBuyClicked()
    {
      if (_offers == null || _selected < 0 || _selected >= _offers.Count)
        return;
      var offer = _offers[_selected];

      var region = offer.HubRegion;
      if (region < 0 || region >= PlayerGold.currency.Length)
      {
        NotificationUi.instance.ShowNotification("Invalid port currency.");
        return;
      }
      if (PlayerGold.currency[region] < offer.Price)
      {
        NotificationUi.instance.ShowNotification("Not enough money for this route guide.");
        return;
      }

      PlayerGold.currency[region] -= offer.Price;

      var port = Port.ports != null && _hubPortIndex >= 0 && _hubPortIndex < Port.ports.Length
        ? Port.ports[_hubPortIndex]
        : null;
      var spawnPos = port != null
        ? port.transform.position + port.transform.forward * 1.2f + Vector3.up * 1.1f
        : Vector3.zero;

      RouteParchmentFactory.Spawn(spawnPos, Quaternion.identity, offer.Pages);

      var profitD = offer.DisplayProfit > 0 ? offer.DisplayProfit : offer.Profit;
      var cur = PlayerGold.GetCurrencyName(offer.HubRegion);
      NotificationUi.instance.ShowNotification(
        "Route guide: " + offer.Tier + " " + offer.Kind + " (~" + profitD + " " + cur + " profit est.)");
      UISoundPlayer.instance.PlayWritingSound();
      UISoundPlayer.instance.PlayGoldSound();

      Close();
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

      _titleText = AddText(_panelRect, 30, 20, PanelW - 110, 44, "", 30, TextAnchor.UpperLeft, true);
      AddHDivider(_panelRect, 30, 72, PanelW - 60);

      var (closeBtnGo, closeButton, _) = CreateButton(_panelRect, PanelW - 70, 16, 44, 44, "X", 24);
      closeButton.onClick.AddListener(Close);

      AddVDivider(_panelRect, PanelW / 2, ListStartY - 8, PanelH - ListStartY - 30);

      _detailTitle = AddText(_panelRect, DetailX, ListStartY, DetailW, 40, "", 28, TextAnchor.UpperLeft, true);
      AddText(_panelRect, DetailX, ListStartY + 46, DetailW, 30, "Route:", 20, TextAnchor.UpperLeft, true);
      _detailRoute = AddText(_panelRect, DetailX, ListStartY + 76, DetailW, 90, "", 20, TextAnchor.UpperLeft, false, false);
      AddText(_panelRect, DetailX, ListStartY + 176, DetailW, 30, "Currency:", 20, TextAnchor.UpperLeft, true);
      _detailCurrency = AddText(_panelRect, DetailX, ListStartY + 206, DetailW, 30, "", 20, TextAnchor.UpperLeft, false);

      var statsY = ListStartY + 254;
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
      int fontSize, TextAnchor anchor, bool bold, bool singleLine = true)
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
      uiText.color = InkColor;
      uiText.supportRichText = true;
      uiText.text = bold ? "<b>" + text + "</b>" : text;
      uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
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
