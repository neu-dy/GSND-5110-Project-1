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
        public float spin;
    }
    private readonly List<Fragment> fragments = new List<Fragment>();

    public void Burst(Vector2 viewport, Color color, int count, float lifetime)
    {
        RectTransform parent = (RectTransform)transform;
        Vector2 origin = Vector2.Scale(viewport - parent.pivot, parent.rect.size);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Swallowed fragment", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = parent.pivot;
            rect.anchoredPosition = origin + Random.insideUnitCircle * 12f;
            rect.sizeDelta = Vector2.one * Random.Range(4f, 10f);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            color.a = 1f;
            image.color = color;
            fragments.Add(new Fragment { rect = rect, image = image, color = color,
                velocity = new Vector2(Random.Range(-150f, 100f), Random.Range(-90f, 150f)),
                lifetime = Mathf.Max(0.1f, lifetime) * Random.Range(0.7f, 1f), spin = Random.Range(-240f, 240f) });
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
            f.velocity += new Vector2(-130f, -80f) * Time.unscaledDeltaTime;
            f.rect.anchoredPosition += f.velocity * Time.unscaledDeltaTime;
            f.rect.Rotate(0, 0, f.spin * Time.unscaledDeltaTime);
            f.rect.localScale = Vector3.one * (1f - f.age / f.lifetime * 0.8f);
            Color color = f.color;
            color.a = 1f - f.age / f.lifetime;
            f.image.color = color;
        }
    }
}
