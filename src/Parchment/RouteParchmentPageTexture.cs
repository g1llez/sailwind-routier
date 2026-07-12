using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Routier
{
  /// <summary>
  /// Renders parchment pages to textures using a UI Canvas (pixel-based layout).
  /// All coordinates below are plain pixels measured from the top-left corner of the
  /// page texture (Width x Height) — no camera zoom/aspect math involved. Text boxes
  /// wrap/clip to their given width instead of overflowing off the page. Column
  /// dividers sit exactly on the boundary between two column boxes, which are padded
  /// symmetrically so the line reads as centered in the gap.
  /// </summary>
  internal static class RouteParchmentPageTexture
  {
    private const int Width = 1024;
    private const int Height = 1536;
    private const int RenderLayer = 31;
    private const int MarginX = 70;
    private const int ContentWidth = Width - MarginX * 2;
    private const int ColPad = 20;
    private static Font _font;
    private static readonly Color ParchmentInk = new Color(0.06f, 0.04f, 0.02f, 1f);

    private static class ReceiptLayout
    {
      internal const int HeaderLabelY = 96;
      internal const int TitleY = 130;
      internal const int RuleY1 = 202;
      internal const int RuleY2 = 210;
      internal const int SubtitleY = 230;
      internal const int GridStartY = 288;
      internal const int GridRowStep = 46;
      internal const int GridColBX = MarginX + ContentWidth / 2;
      internal const int FontSizeTitle = 54;
      internal const int FontSizeBody = 40;
      internal const int FontSizeSmall = 34;
      internal const int FontSizeLabel = 28;
      internal const int FontSizeHeader = 32;
      internal const int RowStep = 58;
      internal const int HeaderGap = 56;
      internal const int RuleGap = 42;
      internal const int EmptyGap = 56;
      internal const int SectionGap = 80;

      // Column boundaries (where the vertical divider sits) — evenly padded on both sides.
      internal const int Boundary1 = 410;
      internal const int Boundary2 = 540;
      internal const int Boundary3 = 760;
      internal const int RightEdge = Width - MarginX;

      internal const int ItemX = MarginX;
      internal const int ItemW = Boundary1 - MarginX - ColPad;
      internal const int AmountX = Boundary1 + ColPad;
      internal const int AmountW = Boundary2 - Boundary1 - ColPad * 2;
      internal const int AvgX = Boundary2 + ColPad;
      internal const int AvgW = Boundary3 - Boundary2 - ColPad * 2;
      internal const int TotalX = Boundary3 + ColPad;
      internal const int TotalW = RightEdge - Boundary3 - ColPad;
    }

    private static class SummaryLayout
    {
      internal const int TitleY = 110;
      internal const int RuleY1 = 182;
      internal const int RuleY2 = 190;
      internal const int HubY = 210;
      internal const int RouteLabelY = 276;
      internal const int RouteStartY = 324;
      internal const int RouteLineStep = 46;
      internal const int FontSizeTitle = 54;
      internal const int FontSizeBody = 40;
      internal const int RowStep = 56;
      internal const int LabelX = MarginX;
      internal const int LabelW = 460;
      internal const int Boundary = LabelX + LabelW;
      internal const int ValueX = Boundary + ColPad;
      internal const int ValueW = Width - MarginX - ValueX;
    }

    internal static Font GetFont()
    {
      if (_font != null)
        return _font;

      var lookUi = Object.FindObjectOfType<LookUI>();
      if (lookUi != null)
      {
        var extra = AccessTools.Field(typeof(LookUI), "extraText").GetValue(lookUi) as TextMesh;
        if (extra?.font != null)
          _font = extra.font;
        if (_font == null)
        {
          var controls = AccessTools.Field(typeof(LookUI), "controlsText").GetValue(lookUi) as TextMesh;
          if (controls?.font != null)
            _font = controls.font;
        }
      }

      if (_font == null)
      {
        foreach (var tm in Object.FindObjectsOfType<TextMesh>())
        {
          if (tm.font == null)
            continue;
          _font = tm.font;
          break;
        }
      }

      if (_font == null)
        _font = Font.CreateDynamicFontFromOSFont("Arial", 32);

      return _font;
    }

    internal static Texture2D Build(ParchmentPage page)
    {
      if (page is ParchmentSummaryPage summary)
        return BuildSummary(summary);
      if (page is ParchmentReceiptPage receipt)
        return BuildReceipt(receipt);
      var text = page is ParchmentTextPage tp ? tp.Text : "";
      return BuildText(text);
    }

    internal static Texture2D BuildText(string text)
    {
      var (root, cam, rt, canvasRect) = CreateRenderRoot();
      AddText(canvasRect, MarginX, 80, ContentWidth, Height - 160, text ?? "", 44, TextAnchor.UpperLeft, false, false);
      return Capture(cam, rt, root);
    }

    internal static Texture2D BuildSummary(ParchmentSummaryPage page)
    {
      var (root, cam, rt, canvasRect) = CreateRenderRoot();

      AddText(canvasRect, MarginX, SummaryLayout.TitleY, ContentWidth, 64, page.Title,
        SummaryLayout.FontSizeTitle, TextAnchor.UpperLeft, true);
      AddHDivider(canvasRect, MarginX, SummaryLayout.RuleY1, ContentWidth);
      AddHDivider(canvasRect, MarginX, SummaryLayout.RuleY2, ContentWidth);
      AddText(canvasRect, MarginX, SummaryLayout.HubY, ContentWidth, 50, page.HubName,
        SummaryLayout.FontSizeBody, TextAnchor.UpperLeft, false);
      AddText(canvasRect, MarginX, SummaryLayout.RouteLabelY, ContentWidth, 46, "Route:",
        SummaryLayout.FontSizeBody, TextAnchor.UpperLeft, true);

      var y = SummaryLayout.RouteStartY;
      if (page.RouteLines != null)
      {
        foreach (var line in page.RouteLines)
        {
          AddText(canvasRect, MarginX, y, ContentWidth, 44, line, SummaryLayout.FontSizeBody, TextAnchor.UpperLeft, false);
          y += SummaryLayout.RouteLineStep;
        }
      }

      y += 40;
      if (!string.IsNullOrEmpty(page.Currency))
      {
        AddText(canvasRect, MarginX, y, ContentWidth, 44, "Currency:", SummaryLayout.FontSizeBody, TextAnchor.UpperLeft, true);
        y += SummaryLayout.RouteLineStep;
        AddText(canvasRect, MarginX, y, ContentWidth, 44, page.Currency, SummaryLayout.FontSizeBody, TextAnchor.UpperLeft, false);
        y += SummaryLayout.RouteLineStep;
      }

      y += 40;
      var tableTop = y;
      var dataBottom = tableTop;
      if (page.Stats != null && page.Stats.Count > 0)
      {
        for (var i = 0; i < page.Stats.Count; i++)
        {
          var rowY = tableTop + SummaryLayout.RowStep * i;
          var row = page.Stats[i];
          AddText(canvasRect, SummaryLayout.LabelX, rowY, SummaryLayout.LabelW - ColPad, 44, row.Label,
            SummaryLayout.FontSizeBody, TextAnchor.UpperLeft, false);
          AddText(canvasRect, SummaryLayout.ValueX, rowY, SummaryLayout.ValueW, 44, row.Value,
            SummaryLayout.FontSizeBody, TextAnchor.UpperRight, false);
          dataBottom = rowY + 44;
        }

        AddVDivider(canvasRect, SummaryLayout.Boundary, tableTop - 6, dataBottom - tableTop + 6);
      }

      if (!string.IsNullOrEmpty(page.GrandTotalLine))
      {
        var ruleY = dataBottom + 20;
        AddHDivider(canvasRect, MarginX, ruleY, ContentWidth);
        AddHDivider(canvasRect, MarginX, ruleY + 8, ContentWidth);
        AddText(canvasRect, MarginX, ruleY + 26, ContentWidth, 46, page.GrandTotalLine,
          SummaryLayout.FontSizeBody, TextAnchor.UpperLeft, true);
      }

      return Capture(cam, rt, root);
    }

    internal static Texture2D BuildReceipt(ParchmentReceiptPage page)
    {
      var (root, cam, rt, canvasRect) = CreateRenderRoot();

      if (!string.IsNullOrEmpty(page.HeaderLabel))
      {
        AddText(canvasRect, MarginX, ReceiptLayout.HeaderLabelY, ContentWidth, 36, page.HeaderLabel,
          ReceiptLayout.FontSizeLabel, TextAnchor.UpperLeft, true);
      }
      AddText(canvasRect, MarginX, ReceiptLayout.TitleY, ContentWidth, 64, page.Title,
        ReceiptLayout.FontSizeTitle, TextAnchor.UpperLeft, true);
      AddHDivider(canvasRect, MarginX, ReceiptLayout.RuleY1, ContentWidth);
      AddHDivider(canvasRect, MarginX, ReceiptLayout.RuleY2, ContentWidth);
      AddText(canvasRect, MarginX, ReceiptLayout.SubtitleY, ContentWidth, 50, page.Subtitle,
        ReceiptLayout.FontSizeBody, TextAnchor.UpperLeft, false);

      var y = ReceiptLayout.GridStartY;
      if (page.LegStats != null && page.LegStats.Count > 0)
      {
        // Read left-to-right, top-to-bottom as a 2x2 grid: distance, cargo | weight, volume.
        for (var i = 0; i < page.LegStats.Count; i++)
        {
          var col = i % 2;
          var row = i / 2;
          var cellX = col == 0 ? MarginX : ReceiptLayout.GridColBX;
          var cellY = y + row * ReceiptLayout.GridRowStep;
          var cellW = (col == 0 ? ReceiptLayout.GridColBX : Width - MarginX) - cellX - ColPad;
          var stat = page.LegStats[i];
          AddText(canvasRect, cellX, cellY, cellW, 44, stat.Label + ": " + stat.Value,
            ReceiptLayout.FontSizeSmall, TextAnchor.UpperLeft, false);
        }
        var rows = Mathf.CeilToInt(page.LegStats.Count / 2f);
        y += rows * ReceiptLayout.GridRowStep;
      }

      y += ReceiptLayout.SectionGap;
      y = DrawSection(canvasRect, page.Sells, y);
      y += ReceiptLayout.SectionGap;
      y = DrawSection(canvasRect, page.Buys, y);

      if (!string.IsNullOrEmpty(page.TotalLine))
      {
        AddText(canvasRect, ReceiptLayout.TotalX - 200, y + 30, ReceiptLayout.TotalW + 200, 46, page.TotalLine,
          ReceiptLayout.FontSizeBody, TextAnchor.UpperRight, true);
      }

      return Capture(cam, rt, root);
    }

    private static int DrawSection(RectTransform canvasRect, ReceiptSection section, int sectionY)
    {
      AddText(canvasRect, MarginX, sectionY, ContentWidth, 46, section.Title,
        ReceiptLayout.FontSizeBody, TextAnchor.UpperLeft, true);

      if (section.Rows == null || section.Rows.Count == 0)
      {
        var empty = string.IsNullOrEmpty(section.EmptyText) ? "None." : section.EmptyText;
        AddText(canvasRect, MarginX, sectionY + ReceiptLayout.EmptyGap, ContentWidth, 46, empty,
          ReceiptLayout.FontSizeBody, TextAnchor.UpperLeft, false);
        return sectionY + ReceiptLayout.EmptyGap + 50;
      }

      var headerY = sectionY + ReceiptLayout.HeaderGap;
      AddText(canvasRect, ReceiptLayout.ItemX, headerY, ReceiptLayout.ItemW, 40, "item",
        ReceiptLayout.FontSizeHeader, TextAnchor.UpperLeft, true);
      AddText(canvasRect, ReceiptLayout.AmountX, headerY, ReceiptLayout.AmountW, 40, "Qty",
        ReceiptLayout.FontSizeHeader, TextAnchor.UpperRight, true);
      AddText(canvasRect, ReceiptLayout.AvgX, headerY, ReceiptLayout.AvgW, 40, "avg. price",
        ReceiptLayout.FontSizeHeader, TextAnchor.UpperRight, true);
      AddText(canvasRect, ReceiptLayout.TotalX, headerY, ReceiptLayout.TotalW, 40, "total",
        ReceiptLayout.FontSizeHeader, TextAnchor.UpperRight, true);

      var ruleY = headerY + ReceiptLayout.RuleGap;
      AddHDivider(canvasRect, ReceiptLayout.ItemX, ruleY, ReceiptLayout.RightEdge - ReceiptLayout.ItemX);

      var dataBottom = ruleY;
      for (var r = 0; r < section.Rows.Count; r++)
      {
        var rowY = ruleY + 14 + ReceiptLayout.RowStep * r;
        var row = section.Rows[r];
        var amount = row.Amount > 0 ? "+" + row.Amount : row.Amount.ToString();
        var total = row.Amount < 0 ? "+" + row.TotalMagnitude : "-" + row.TotalMagnitude;

        AddText(canvasRect, ReceiptLayout.ItemX, rowY, ReceiptLayout.ItemW, 44, row.Name,
          ReceiptLayout.FontSizeBody, TextAnchor.UpperLeft, false);
        AddText(canvasRect, ReceiptLayout.AmountX, rowY, ReceiptLayout.AmountW, 44, amount,
          ReceiptLayout.FontSizeBody, TextAnchor.UpperRight, false);
        AddText(canvasRect, ReceiptLayout.AvgX, rowY, ReceiptLayout.AvgW, 44, Mathf.RoundToInt(row.AvgPrice).ToString(),
          ReceiptLayout.FontSizeBody, TextAnchor.UpperRight, false);
        AddText(canvasRect, ReceiptLayout.TotalX, rowY, ReceiptLayout.TotalW, 44, total,
          ReceiptLayout.FontSizeBody, TextAnchor.UpperRight, false);
        dataBottom = rowY + 44;
      }

      var lineTop = headerY - 4;
      var lineHeight = dataBottom - lineTop + 6;
      AddVDivider(canvasRect, ReceiptLayout.Boundary1, lineTop, lineHeight);
      AddVDivider(canvasRect, ReceiptLayout.Boundary2, lineTop, lineHeight);
      AddVDivider(canvasRect, ReceiptLayout.Boundary3, lineTop, lineHeight);

      return dataBottom + 20;
    }

    private static (GameObject root, Camera cam, RenderTexture rt, RectTransform canvasRect) CreateRenderRoot()
    {
      var root = new GameObject("RoutierPageRT");
      root.hideFlags = HideFlags.HideAndDontSave;

      var rt = new RenderTexture(Width, Height, 16, RenderTextureFormat.ARGB32);
      rt.Create();

      var camGo = new GameObject("Cam");
      camGo.transform.SetParent(root.transform, false);
      camGo.layer = RenderLayer;
      var cam = camGo.AddComponent<Camera>();
      cam.cullingMask = 1 << RenderLayer;
      cam.targetTexture = rt;
      cam.clearFlags = CameraClearFlags.SolidColor;
      cam.backgroundColor = Color.clear;
      cam.orthographic = true;
      cam.orthographicSize = 5f;
      cam.nearClipPlane = 0.1f;
      cam.farClipPlane = 20f;
      cam.transform.localPosition = new Vector3(0f, 0f, -10f);

      var canvasGo = new GameObject("Canvas", typeof(RectTransform));
      canvasGo.layer = RenderLayer;
      canvasGo.transform.SetParent(root.transform, false);
      var canvas = canvasGo.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = cam;
      canvas.planeDistance = 5f;

      var canvasRect = canvasGo.GetComponent<RectTransform>();
      canvasRect.sizeDelta = new Vector2(Width, Height);

      return (root, cam, rt, canvasRect);
    }

    private static Texture2D Capture(Camera cam, RenderTexture rt, GameObject root)
    {
      Canvas.ForceUpdateCanvases();
      cam.Render();

      var prev = RenderTexture.active;
      RenderTexture.active = rt;
      var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
      tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
      tex.Apply();
      RenderTexture.active = prev;

      Object.Destroy(root);
      rt.Release();
      Object.Destroy(rt);

      tex.wrapMode = TextureWrapMode.Clamp;
      tex.filterMode = FilterMode.Bilinear;
      return tex;
    }

    /// <summary>
    /// Adds a pixel-anchored text box (top-left origin).
    /// When <paramref name="singleLine"/> is true (the default — used for every table cell, header,
    /// and label), the text never wraps to a second line: if it's too wide for the box, Unity's
    /// "best fit" shrinks the font just enough to stay on one line, so long item/port names can
    /// never bleed down into the row below. Pass false only for genuine multi-line free text.
    /// </summary>
    private static void AddText(
      RectTransform canvasRect, int xPx, int yFromTopPx, int widthPx, int heightPx, string text,
      int fontSize, TextAnchor anchor, bool bold, bool singleLine = true)
    {
      if (string.IsNullOrEmpty(text))
        return;

      var go = new GameObject("Txt", typeof(RectTransform));
      go.layer = canvasRect.gameObject.layer;
      go.transform.SetParent(canvasRect, false);

      var rt = go.GetComponent<RectTransform>();
      rt.anchorMin = new Vector2(0f, 1f);
      rt.anchorMax = new Vector2(0f, 1f);
      rt.pivot = new Vector2(0f, 1f);
      rt.anchoredPosition = new Vector2(xPx, -yFromTopPx);
      // For single-line cells the box height is capped to exactly one line at fontSize: this is what
      // makes "best fit" actually shrink for WIDTH — with Overflow, Best Fit never wraps to test width
      // at all and just renders at max size, so a too-wide word would bleed past the box. With Wrap +
      // a one-line-tall box, any text too wide to fit on one line would need a 2nd line that doesn't
      // fit the box height, so Best Fit keeps shrinking the font until it fits on a single line.
      rt.sizeDelta = new Vector2(widthPx, singleLine ? fontSize + 6 : heightPx);

      var uiText = go.AddComponent<Text>();
      uiText.font = GetFont();
      uiText.fontSize = fontSize;
      uiText.alignment = anchor;
      uiText.color = ParchmentInk;
      uiText.supportRichText = true;
      uiText.text = bold ? "<b>" + text + "</b>" : text;

      if (singleLine)
      {
        uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
        uiText.verticalOverflow = VerticalWrapMode.Truncate;
        uiText.resizeTextForBestFit = true;
        uiText.resizeTextMinSize = 14;
        uiText.resizeTextMaxSize = fontSize;
      }
      else
      {
        uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
        uiText.verticalOverflow = VerticalWrapMode.Overflow;
      }
    }

    /// <summary>Adds a thin vertical divider line (top-left origin, height in pixels).</summary>
    private static void AddVDivider(RectTransform canvasRect, int xPx, int yFromTopPx, int heightPx)
    {
      var go = new GameObject("VDivider", typeof(RectTransform));
      go.layer = canvasRect.gameObject.layer;
      go.transform.SetParent(canvasRect, false);

      var rt = go.GetComponent<RectTransform>();
      rt.anchorMin = new Vector2(0f, 1f);
      rt.anchorMax = new Vector2(0f, 1f);
      rt.pivot = new Vector2(0f, 1f);
      rt.anchoredPosition = new Vector2(xPx, -yFromTopPx);
      rt.sizeDelta = new Vector2(2f, Mathf.Max(2, heightPx));

      var img = go.AddComponent<Image>();
      img.color = new Color(ParchmentInk.r, ParchmentInk.g, ParchmentInk.b, 0.55f);
    }

    /// <summary>Adds a thin horizontal divider line (top-left origin, width in pixels).</summary>
    private static void AddHDivider(RectTransform canvasRect, int xPx, int yFromTopPx, int widthPx)
    {
      var go = new GameObject("HDivider", typeof(RectTransform));
      go.layer = canvasRect.gameObject.layer;
      go.transform.SetParent(canvasRect, false);

      var rt = go.GetComponent<RectTransform>();
      rt.anchorMin = new Vector2(0f, 1f);
      rt.anchorMax = new Vector2(0f, 1f);
      rt.pivot = new Vector2(0f, 1f);
      rt.anchoredPosition = new Vector2(xPx, -yFromTopPx);
      rt.sizeDelta = new Vector2(Mathf.Max(2, widthPx), 2f);

      var img = go.AddComponent<Image>();
      img.color = new Color(ParchmentInk.r, ParchmentInk.g, ParchmentInk.b, 0.55f);
    }
  }
}
