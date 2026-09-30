using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The menu hints stay intact until they hit the bottom of the screen. Their
// background and glyphs are separate falling bodies, then separate debris.
public sealed class MenuControlCollapse
{
    private const float FloorInset = .08f;
    private const float BackdropStart = .06f;
    private const float BackdropFall = .52f;
    private const float TextStart = .16f;
    private const float TextStagger = .045f;
    private const float TextFall = .49f;
    private readonly RectTransform root;
    private readonly RectTransform backdrop;
    private readonly Image backdropImage;
    private readonly TMP_Text[] hints;
    private readonly Vector2 backdropPosition;
    private readonly Vector2[] hintPositions;
    private readonly Color backdropColor;
    private readonly Color[] hintColors;
    private readonly bool[] textBroken;
    private readonly MenuControlDebrisGraphic debris;
    private bool backdropBroken;

    public MenuControlCollapse(RectTransform root, RectTransform backdrop, TMP_Text[] hints)
    {
        this.root = root;
        this.backdrop = backdrop;
        this.hints = hints;
        backdropImage = backdrop.GetComponent<Image>();
        backdropPosition = backdrop.anchoredPosition;
        backdropColor = backdropImage.color;
        hintPositions = new Vector2[hints.Length];
        hintColors = new Color[hints.Length];
        textBroken = new bool[hints.Length];
        for (int i = 0; i < hints.Length; i++)
        {
            hintPositions[i] = hints[i].rectTransform.anchoredPosition;
            hintColors[i] = hints[i].color;
        }
        var debrisObject = new GameObject("Menu Hint Debris", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(MenuControlDebrisGraphic));
        debrisObject.transform.SetParent(root, false);
        RectTransform debrisRect = debrisObject.GetComponent<RectTransform>();
        debrisRect.anchorMin = Vector2.zero;
        debrisRect.anchorMax = Vector2.one;
        debrisRect.offsetMin = debrisRect.offsetMax = Vector2.zero;
        debris = debrisObject.GetComponent<MenuControlDebrisGraphic>();
        debris.raycastTarget = false;
    }

    public void Begin()
    {
        debris.ResetDebris();
        backdropBroken = false;
        backdrop.anchoredPosition = backdropPosition;
        backdrop.localRotation = Quaternion.identity;
        backdrop.localScale = Vector3.one;
        backdropImage.color = backdropColor;
        backdropImage.enabled = true;
        for (int i = 0; i < hints.Length; i++)
        {
            textBroken[i] = false;
            hints[i].gameObject.SetActive(true);
            hints[i].rectTransform.anchoredPosition = hintPositions[i];
            hints[i].rectTransform.localRotation = Quaternion.identity;
            hints[i].rectTransform.localScale = Vector3.one;
            hints[i].color = hintColors[i];
        }
    }

    public void Tick(float elapsed, float deltaTime)
    {
        float floor = root.rect.yMin + root.rect.height * FloorInset;
        if (!backdropBroken)
        {
            float fall = Mathf.Clamp01((elapsed - BackdropStart) / BackdropFall);
            MoveToFloor(backdrop, backdropPosition, fall, floor, -5f);
            if (fall >= 1f)
            {
                backdropBroken = true;
                BreakBackdrop();
                backdropImage.enabled = false;
            }
        }

        for (int i = 0; i < hints.Length; i++)
        {
            if (textBroken[i]) continue;
            float fall = Mathf.Clamp01((elapsed - TextStart - i * TextStagger) / TextFall);
            MoveToFloor(hints[i].rectTransform, hintPositions[i], fall, floor,
                (i % 2 == 0 ? -1f : 1f) * (3f + i * .7f));
            if (fall < 1f) continue;
            textBroken[i] = true;
            BreakText(hints[i], i);
            hints[i].gameObject.SetActive(false);
        }

        debris.Tick(deltaTime, floor);
    }

    private static void MoveToFloor(RectTransform rect, Vector2 start, float fall,
        float floor, float tipAngle)
    {
        float anchorY = Mathf.Lerp(rect.parent.GetComponent<RectTransform>().rect.yMin,
            rect.parent.GetComponent<RectTransform>().rect.yMax, rect.anchorMin.y);
        float targetY = floor + rect.rect.height * rect.pivot.y - anchorY;
        float descent = fall * fall;
        rect.anchoredPosition = new Vector2(start.x, Mathf.Lerp(start.y, targetY, descent));
        rect.localRotation = Quaternion.Euler(0f, 0f, tipAngle * descent);
    }

    private void BreakBackdrop()
    {
        const int columns = 10;
        const int rows = 5;
        Rect area = backdrop.rect;
        float width = area.width / columns;
        float height = area.height / rows;
        var joints = new Vector2[columns + 1, rows + 1];
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            float jitterX = x == 0 || x == columns ? 0f : (Hash01(x * 17 + y * 29) - .5f) * width * .46f;
            float jitterY = y == 0 || y == rows ? 0f : (Hash01(x * 31 + y * 13) - .5f) * height * .46f;
            joints[x, y] = new Vector2(area.xMin + x * width + jitterX,
                area.yMin + y * height + jitterY);
        }

        Vector2 panelCenter = ToMenuPoint(area.center);
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            Vector2 lowerLeft = joints[x, y];
            Vector2 lowerRight = joints[x + 1, y];
            Vector2 upperLeft = joints[x, y + 1];
            Vector2 upperRight = joints[x + 1, y + 1];
            int cell = y * columns + x;
            Vector2 center = (lowerLeft + lowerRight + upperLeft + upperRight) * .25f
                + new Vector2((Hash01(cell * 7 + 1) - .5f) * width * .3f,
                    (Hash01(cell * 11 + 2) - .5f) * height * .3f);
            AddBackdropTriangle(lowerLeft, lowerRight, center, panelCenter, cell * 4);
            AddBackdropTriangle(lowerRight, upperRight, center, panelCenter, cell * 4 + 1);
            AddBackdropTriangle(upperRight, upperLeft, center, panelCenter, cell * 4 + 2);
            AddBackdropTriangle(upperLeft, lowerLeft, center, panelCenter, cell * 4 + 3);
        }
    }

    private void AddBackdropTriangle(Vector2 localA, Vector2 localB, Vector2 localC,
        Vector2 panelCenter, int seed)
    {
        Vector2 a = ToMenuPoint(localA);
        Vector2 b = ToMenuPoint(localB);
        Vector2 c = ToMenuPoint(localC);
        Vector2 center = (a + b + c) / 3f;
        float noise = Hash01(seed * 19 + 7) * 2f - 1f;
        Vector2 velocity = new Vector2((center.x - panelCenter.x) * .28f + noise * 52f,
            80f + Hash01(seed * 23 + 11) * 120f);
        Color tint = backdropColor * Mathf.Lerp(.8f, 1.17f, Hash01(seed * 13 + 3));
        tint.a = backdropColor.a;
        debris.AddTriangle(a, b, c, tint, velocity, noise * 240f, 1.1f);
    }

    private Vector2 ToMenuPoint(Vector2 backdropLocal)
    {
        return root.InverseTransformPoint(backdrop.TransformPoint(backdropLocal));
    }

    private void BreakText(TMP_Text source, int line)
    {
        source.ForceMeshUpdate();
        TMP_TextInfo textInfo = source.textInfo;
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo glyph = textInfo.characterInfo[i];
            if (!glyph.isVisible) continue;
            for (int piece = 0; piece < 7; piece++)
            {
                int seed = line * 701 + i * 17 + piece;
                float u = .13f + Hash01(seed * 29 + 3) * .74f;
                float v = .13f + Hash01(seed * 31 + 9) * .74f;
                Vector3 local = Vector3.Lerp(glyph.bottomLeft, glyph.topRight, u);
                local.y = Mathf.Lerp(glyph.bottomLeft.y, glyph.topRight.y, v);
                Vector2 center = root.InverseTransformPoint(source.rectTransform.TransformPoint(local));
                float size = 1.6f + Hash01(seed * 37 + 5) * 3.2f;
                float skew = (Hash01(seed * 41 + 2) - .5f) * size;
                Vector2 a = center + new Vector2(-size * .55f, -size * .3f);
                Vector2 b = center + new Vector2(size * .5f, -size * .2f + skew * .25f);
                Vector2 c = center + new Vector2(skew * .45f, size * .6f);
                float sideways = (Hash01(seed * 43 + 1) - .5f) * 115f;
                Vector2 velocity = new Vector2(sideways, 90f + Hash01(seed * 47 + 6) * 120f);
                Color tint = source.color;
                tint.a *= Mathf.Lerp(.65f, 1f, Hash01(seed * 53 + 4));
                debris.AddTriangle(a, b, c, tint, velocity,
                    (Hash01(seed * 59 + 8) - .5f) * 460f, .72f);
            }
        }
    }

    private static float Hash01(int seed)
    {
        return Mathf.Repeat(Mathf.Sin(seed * 78.233f + 19.19f) * 43758.5453f, 1f);
    }
}
