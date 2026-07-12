using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace Routier
{
  /// <summary>Route guide pages rendered as textures on a vanilla ShipItemScroll.</summary>
  internal sealed class RouteParchmentOverlay : MonoBehaviour
  {
    private static readonly AccessTools.FieldRef<ShipItemScroll, Renderer> PageField =
      AccessTools.FieldRefAccess<ShipItemScroll, Renderer>("page");
    private static readonly AccessTools.FieldRef<ShipItemScroll, GameObject> ArrowUpField =
      AccessTools.FieldRefAccess<ShipItemScroll, GameObject>("arrowUp");
    private static readonly AccessTools.FieldRef<ShipItemScroll, GameObject> ArrowDownField =
      AccessTools.FieldRefAccess<ShipItemScroll, GameObject>("arrowDown");
    private static readonly AccessTools.FieldRef<ShipItemScroll, MeshFilter> FilterField =
      AccessTools.FieldRefAccess<ShipItemScroll, MeshFilter>("filter");
    private static readonly AccessTools.FieldRef<ShipItemScroll, int> CurrentPageField =
      AccessTools.FieldRefAccess<ShipItemScroll, int>("currentPage");
    private static readonly AccessTools.FieldRef<ShipItemScroll, int> PageCountField =
      AccessTools.FieldRefAccess<ShipItemScroll, int>("pageCount");
    private static readonly AccessTools.FieldRef<ShipItemScroll, Mesh> ClosedMeshField =
      AccessTools.FieldRefAccess<ShipItemScroll, Mesh>("closedMesh");

    private ParchmentPage[] _pages;
    private Texture2D[] _textures;
    private ShipItemScroll _scroll;
    private Renderer _page;
    private bool _visible;
    private Coroutine _buildRoutine;

    internal void Bind(ParchmentPage[] pages, ShipItemScroll scroll)
    {
      _pages = pages ?? new ParchmentPage[0];
      _scroll = scroll;
      _page = PageField(scroll);

      scroll.sold = true;
      scroll.name = "Route Guide";
      scroll.lookText = "Route guide (scroll to turn pages)";
      scroll.value = 50;
    }

    internal void SetupOnLoad()
    {
      if (_scroll == null)
        _scroll = GetComponent<ShipItemScroll>();
      if (_page == null && _scroll != null)
        _page = PageField(_scroll);

      var filter = _scroll.GetComponent<MeshFilter>();
      FilterField(_scroll) = filter;
      ClosedMeshField(_scroll) = filter != null ? filter.sharedMesh : null;

      PageCountField(_scroll) = _pages.Length;
      CurrentPageField(_scroll) = 0;

      if (_page != null)
        _page.enabled = false;

      HideArrows();
      _buildRoutine = StartCoroutine(BuildTextures());
    }

    internal void Show()
    {
      _visible = true;
      if (_textures != null && _textures.Length > 0)
        ApplyPageTexture(CurrentPageField(_scroll));
      UpdateArrows();
    }

    internal void Hide()
    {
      _visible = false;
      if (_page != null)
        _page.enabled = false;
      HideArrows();
    }

    internal void HandleScroll(float input)
    {
      if (_pages == null || _pages.Length == 0 || _textures == null || _textures.Length == 0)
        return;

      var page = CurrentPageField(_scroll);
      if (input > 0f && page > 0)
      {
        CurrentPageField(_scroll) = page - 1;
        ApplyPageTexture(page - 1);
        UpdateArrows();
        if (UISoundPlayer.instance != null)
          UISoundPlayer.instance.PlayParchmentSound();
      }
      else if (input < 0f && page < _pages.Length - 1)
      {
        CurrentPageField(_scroll) = page + 1;
        ApplyPageTexture(page + 1);
        UpdateArrows();
        if (UISoundPlayer.instance != null)
          UISoundPlayer.instance.PlayParchmentSound();
      }
    }

    private IEnumerator BuildTextures()
    {
      yield return null;

      if (_pages == null || _pages.Length == 0)
        yield break;

      _textures = new Texture2D[_pages.Length];
      for (var i = 0; i < _pages.Length; i++)
      {
        _textures[i] = RouteParchmentPageTexture.Build(_pages[i]);
        yield return null;
      }

      if (_visible)
        ApplyPageTexture(CurrentPageField(_scroll));
    }

    private void ApplyPageTexture(int index)
    {
      if (!_visible || _page == null || _textures == null || _textures.Length == 0)
        return;

      index = Mathf.Clamp(index, 0, _textures.Length - 1);
      if (_textures[index] == null)
        return;

      if (_page.material != null)
      {
        var mat = _page.material;
        mat.mainTexture = _textures[index];
        EnablePageAlphaBlend(mat);
      }
      _page.enabled = true;
    }

    private static void EnablePageAlphaBlend(Material mat)
    {
      if (mat == null || !mat.HasProperty("_Mode"))
        return;
      mat.SetFloat("_Mode", 3f);
      mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
      mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
      mat.SetInt("_ZWrite", 0);
      mat.DisableKeyword("_ALPHATEST_ON");
      mat.EnableKeyword("_ALPHABLEND_ON");
      mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
      mat.renderQueue = 3000;
    }

    private void UpdateArrows()
    {
      if (!_visible)
        return;

      var arrowUp = ArrowUpField(_scroll);
      var arrowDown = ArrowDownField(_scroll);
      if (arrowUp == null || arrowDown == null)
        return;

      var page = CurrentPageField(_scroll);
      var count = PageCountField(_scroll);
      arrowDown.SetActive(page < count - 1);
      arrowUp.SetActive(page > 0);
    }

    private void HideArrows()
    {
      var arrowUp = ArrowUpField(_scroll);
      var arrowDown = ArrowDownField(_scroll);
      if (arrowUp != null)
        arrowUp.SetActive(false);
      if (arrowDown != null)
        arrowDown.SetActive(false);
    }

    private void OnDestroy()
    {
      if (_buildRoutine != null)
        StopCoroutine(_buildRoutine);

      if (_textures == null)
        return;
      foreach (var tex in _textures)
      {
        if (tex != null)
          Destroy(tex);
      }
    }
  }
}
