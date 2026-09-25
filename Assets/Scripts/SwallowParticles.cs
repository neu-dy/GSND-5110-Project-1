using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Screen-space fragments remain visible above the opaque curtain.
public class SwallowParticles : MonoBehaviour
{
    private sealed class Fragment
    {
        public RectTransform rect;
        public Image image;
        public Vector2 velocity;
        public Color color;
        public float age;
        public float lifetime;
        public float size;
        public Vector2 sink;
        public bool swallowed;
        public float spin;
    }
    private readonly List<Fragment> fragments = new List<Fragment>();

    public void Burst(Vector2 viewport, Color color, int count, float lifetime,
        bool swallowed = true, Vector2 viewportSpread = default)
    {
        RectTransform parent = (RectTransform)transform;
        Vector2 origin = Vector2.Scale(viewport - parent.pivot, parent.rect.size);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Swallowed fragment", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = parent.pivot;
            rect.anchoredPosition = origin + (swallowed ? Random.insideUnitCircle * 12f
                : Vector2.Scale(new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f)),
                    Vector2.Scale(viewportSpread, parent.rect.size)));
            rect.sizeDelta = Vector2.one * Random.Range(4f, 10f);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            color.a = 1f;
            image.color = color;
            fragments.Add(new Fragment { rect = rect, image = image, color = color,
                velocity = swallowed ? new Vector2(Random.Range(20f, 85f), Random.Range(-60f, 70f))
                    : Random.insideUnitCircle.normalized * Random.Range(120f, 280f) + new Vector2(-65f, 70f),
                lifetime = Mathf.Max(0.1f, lifetime) * Random.Range(0.7f, 1f), size = rect.sizeDelta.x,
                sink = origin + new Vector2(-Random.Range(100f, 200f), Random.Range(-25f, 25f)),
                swallowed = swallowed, spin = Random.Range(-270f, 270f) });
        }
    }

    void Update()
    {
        for (int i = fragments.Count - 1; i >= 0; i--)
        {
            Fragment f = fragments[i];
            f.age += Time.unscaledDeltaTime;
            if (f.age >= f.lifetime)
            {
                Destroy(f.rect.gameObject);
                fragments.RemoveAt(i);
                continue;
            }
            float progress = f.age / f.lifetime;
            if (!f.swallowed)
            {
                f.velocity += Vector2.down * 260f * Time.unscaledDeltaTime;
                f.rect.anchoredPosition += f.velocity * Time.unscaledDeltaTime;
                f.rect.Rotate(0, 0, f.spin * Time.unscaledDeltaTime);
                f.rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.3f, progress);
                Color fragmentColor = f.color;
                fragmentColor.a = 1f - progress;
                f.image.color = fragmentColor;
                continue;
            }
            // Brief outward spray, then a viscous pull into the dark.
            float pull = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.12f) / 0.55f));
            Vector2 desired = (f.sink - f.rect.anchoredPosition).normalized * Mathf.Lerp(60f, 420f, pull);
            float blend = 1f - Mathf.Exp(-8f * pull * Time.unscaledDeltaTime);
            f.velocity = Vector2.Lerp(f.velocity, desired, blend);
            f.rect.anchoredPosition += f.velocity * Time.unscaledDeltaTime;
            f.rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(f.velocity.y, f.velocity.x) * Mathf.Rad2Deg);
            f.rect.sizeDelta = new Vector2(f.size * Mathf.Lerp(1f, 3.5f, pull), f.size * Mathf.Lerp(1f, 0.2f, pull));
            Color color = f.color;
            color.a = 1f - f.age / f.lifetime;
            f.image.color = color;
        }
    }
}
