using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Routier
{
  internal sealed class ReceiptLine
  {
    public string Name;
    public int Amount;
    public float AvgPrice;
    public int TotalMagnitude;
  }

  internal sealed class SummaryStatRow
  {
    public string Label;
    public string Value;
  }

  internal abstract class ParchmentPage
  {
    internal abstract string Serialize();
    internal static ParchmentPage Deserialize(string raw) => ParchmentPageCodec.Parse(raw);
  }

  internal sealed class ParchmentTextPage : ParchmentPage
  {
    internal string Text = "";

    internal override string Serialize() => "T\x1f" + (Text ?? "").Replace("\n", "\x1e");
  }

  internal sealed class ParchmentSummaryPage : ParchmentPage
  {
    internal string Title = "TRADE ROUTE GUIDE";
    internal string HubName = "";
    internal string Currency = "";
    internal string GrandTotalLine = "";
    internal string[] RouteLines = new string[0];
    internal List<SummaryStatRow> Stats = new List<SummaryStatRow>();

    internal override string Serialize()
    {
      var sb = new StringBuilder();
      sb.Append("S\x1f").Append(Title).Append('\x1f').Append(HubName).Append('\x1f').Append(Currency)
        .Append('\x1f').Append(GrandTotalLine).Append('\x1f').Append(RouteLines.Length);
      foreach (var line in RouteLines)
        sb.Append('\x1f').Append(line);
      sb.Append('\x1f').Append(Stats.Count);
      foreach (var row in Stats)
        sb.Append('\x1f').Append(row.Label).Append('\x1e').Append(row.Value);
      return sb.ToString();
    }
  }

  internal sealed class ParchmentReceiptPage : ParchmentPage
  {
    internal string HeaderLabel = "";
    internal string Title = "";
    internal string Subtitle = "";
    /// <summary>Read left-to-right, top-to-bottom as a 2x2 grid (distance, cargo units, weight, volume).</summary>
    internal List<SummaryStatRow> LegStats = new List<SummaryStatRow>();
    internal ReceiptSection Sells = new ReceiptSection { Title = "CARGO DISCHARGED" };
    internal ReceiptSection Buys = new ReceiptSection { Title = "CARGO LOADED" };
    internal string TotalLine = "";

    internal override string Serialize()
    {
      var sb = new StringBuilder();
      sb.Append("R\x1f").Append(Title).Append('\x1f').Append(Subtitle).Append('\x1f').Append(TotalLine)
        .Append('\x1f').Append(HeaderLabel).Append('\x1f').Append(LegStats.Count);
      foreach (var row in LegStats)
        sb.Append('\x1f').Append(row.Label).Append('\x1e').Append(row.Value);
      AppendSection(sb, Sells);
      AppendSection(sb, Buys);
      return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, ReceiptSection section)
    {
      sb.Append('\x1f').Append(section.Title).Append('\x1f').Append(section.EmptyText ?? "")
        .Append('\x1f').Append(section.Rows.Count);
      foreach (var row in section.Rows)
      {
        sb.Append('\x1f').Append(row.Name).Append('\x1e').Append(row.Amount.ToString(CultureInfo.InvariantCulture))
          .Append('\x1e').Append(row.AvgPrice.ToString("0.0", CultureInfo.InvariantCulture))
          .Append('\x1e').Append(row.TotalMagnitude);
      }
    }
  }

  internal sealed class ReceiptSection
  {
    internal string Title = "";
    internal string EmptyText = "";
    internal List<ReceiptLine> Rows = new List<ReceiptLine>();
  }

  internal static class ParchmentPageCodec
  {
    internal static string[] SerializeAll(ParchmentPage[] pages)
    {
      if (pages == null)
        return new string[0];
      var outArr = new string[pages.Length];
      for (var i = 0; i < pages.Length; i++)
        outArr[i] = pages[i]?.Serialize() ?? "";
      return outArr;
    }

    internal static ParchmentPage[] DeserializeAll(string[] raw)
    {
      if (raw == null)
        return new ParchmentPage[0];
      var pages = new ParchmentPage[raw.Length];
      for (var i = 0; i < raw.Length; i++)
        pages[i] = Parse(raw[i]);
      return pages;
    }

    internal static ParchmentPage Parse(string raw)
    {
      if (string.IsNullOrEmpty(raw))
        return new ParchmentTextPage();
      if (raw.StartsWith("S\x1f"))
        return ParseSummary(raw);
      if (raw.StartsWith("R\x1f"))
        return ParseReceipt(raw);
      if (raw.StartsWith("T\x1f"))
        return new ParchmentTextPage { Text = raw.Substring(2).Replace("\x1e", "\n") };
      return new ParchmentTextPage { Text = raw };
    }

    private static ParchmentSummaryPage ParseSummary(string raw)
    {
      var parts = raw.Split('\x1f');
      var page = new ParchmentSummaryPage();
      if (parts.Length < 6)
        return page;
      page.Title = parts[1];
      page.HubName = parts[2];
      page.Currency = parts[3];
      page.GrandTotalLine = parts[4];
      if (!int.TryParse(parts[5], out var routeCount))
        return page;
      var idx = 6;
      page.RouteLines = new string[routeCount];
      for (var i = 0; i < routeCount && idx < parts.Length; i++, idx++)
        page.RouteLines[i] = parts[idx];
      if (idx >= parts.Length || !int.TryParse(parts[idx], out var statCount))
        return page;
      idx++;
      for (var i = 0; i < statCount && idx < parts.Length; i++, idx++)
      {
        var cells = parts[idx].Split('\x1e');
        if (cells.Length < 2)
          continue;
        page.Stats.Add(new SummaryStatRow { Label = cells[0], Value = cells[1] });
      }
      return page;
    }

    private static ParchmentReceiptPage ParseReceipt(string raw)
    {
      var parts = raw.Split('\x1f');
      var page = new ParchmentReceiptPage();
      if (parts.Length < 6)
        return page;
      page.Title = parts[1];
      page.Subtitle = parts[2];
      page.TotalLine = parts[3];
      page.HeaderLabel = parts[4];
      if (!int.TryParse(parts[5], out var legStatCount))
        return page;
      var idx = 6;
      for (var i = 0; i < legStatCount && idx < parts.Length; i++, idx++)
      {
        var cells = parts[idx].Split('\x1e');
        if (cells.Length < 2)
          continue;
        page.LegStats.Add(new SummaryStatRow { Label = cells[0], Value = cells[1] });
      }
      idx = ParseSection(parts, idx, page.Sells);
      ParseSection(parts, idx, page.Buys);
      return page;
    }

    private static int ParseSection(string[] parts, int idx, ReceiptSection section)
    {
      if (idx + 1 >= parts.Length)
        return idx;
      section.Title = parts[idx++];
      if (idx >= parts.Length)
        return idx;

      // New format: empty text field before count
      if (int.TryParse(parts[idx], out var count))
      {
        section.EmptyText = "";
      }
      else
      {
        section.EmptyText = parts[idx++];
        if (idx >= parts.Length || !int.TryParse(parts[idx], out count))
          return idx;
      }
      idx++;
      for (var i = 0; i < count && idx < parts.Length; i++, idx++)
      {
        var cells = parts[idx].Split('\x1e');
        if (cells.Length < 4)
          continue;
        section.Rows.Add(new ReceiptLine
        {
          Name = cells[0],
          Amount = int.Parse(cells[1], CultureInfo.InvariantCulture),
          AvgPrice = float.Parse(cells[2], CultureInfo.InvariantCulture),
          TotalMagnitude = int.Parse(cells[3], CultureInfo.InvariantCulture),
        });
      }
      return idx;
    }
  }
}
