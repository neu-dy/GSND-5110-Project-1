using UnityEngine;
using UnityEngine.UI;

// This opaque silhouette is separate from the lethal curtain. Only its active
// body can injure, and its sampled shape is shared by drawing and hit checks.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TentacleGraphic : MaskableGraphic
{
    const int Segments=64;
    private float root,tip,center,rootRadius,shaftRadius;
    private bool warning, vertical;
    private float rootAttachment;
    public void SetRootAttachment(float x) { rootAttachment=Mathf.Min(root,x);SetVerticesDirty(); }
    public bool Visible => tip > root+.00001f && shaftRadius > 0;
    public void SetShape(float from,float to,float y,float baseRadius,float bodyRadius,bool isWarning)
    {
        vertical=false;rootAttachment=from;
        root=from;tip=Mathf.Max(from,to);center=y;rootRadius=baseRadius;shaftRadius=bodyRadius;warning=isWarning;
        SetVerticesDirty();
    }
    public void SetVerticalShape(float top,float bottom,float x,float baseRadius,float bodyRadius,bool isWarning)
    {
        SetShape(-top,-bottom,x,baseRadius,bodyRadius,isWarning);
        vertical=true;
    }
    private float Radius(float u)
    {
        // A crest with vertical tangents at the root joins the flat curtain
        // smoothly, instead of looking like a semicircle pasted onto its edge.
        if(warning) return rootRadius*Mathf.Sqrt(Mathf.Max(0,1-Mathf.Sqrt(u)));
        float baseBlend=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(u/.24f));
        float taper=Mathf.Sqrt(Mathf.Clamp01((1-u)/.09f));
        return Mathf.Lerp(shaftRadius,rootRadius,baseBlend)*taper;
    }
    public bool Intersects(Rect bounds)
    {
        if(vertical) bounds=new Rect(-bounds.yMax,bounds.xMin,bounds.height,bounds.width);
        if(!Visible || warning || bounds.xMax<root || bounds.xMin>tip) return false;
        float a=Mathf.Clamp01((bounds.xMin-root)/(tip-root));
        // Radius decreases monotonically from root to tip: this is the largest
        // radius intersecting the rectangle, not a broad enclosing collision box.
        float radius=Radius(a);
        return bounds.yMax>center-radius+.001f && bounds.yMin<center+radius-.001f;
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); if(!Visible)return;
        Rect r=rectTransform.rect;
        for(int i=0;i<=Segments;i++)
        {
            float u=i/(float)Segments, x=Mathf.Lerp(root,tip,u), radius=Radius(u);
            // Only the hidden root seam extends; the strike hit shape is unchanged.
            if(i==0 && !vertical)x=rootAttachment;
            Vector2 a=vertical?new Vector2(center-radius,-x):new Vector2(x,center-radius);
            Vector2 b=vertical?new Vector2(center+radius,-x):new Vector2(x,center+radius);
            mesh.AddVert(new Vector3(r.xMin+a.x*r.width,r.yMin+a.y*r.height),Color.black,Vector2.zero);
            mesh.AddVert(new Vector3(r.xMin+b.x*r.width,r.yMin+b.y*r.height),Color.black,Vector2.zero);
            if(i==0)continue;int v=i*2;
            mesh.AddTriangle(v-2,v,v-1);mesh.AddTriangle(v-1,v,v+1);
        }
    }
}
