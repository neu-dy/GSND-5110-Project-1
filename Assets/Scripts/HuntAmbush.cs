using System;
using TMPro;
using UnityEngine;

// Scene ownership for a single hunt event. Restores every global/input/pose
// override on success, death, cancellation, disable and scene exit.
public sealed class HuntAmbush
{
    public HuntAmbushState State { get; private set; }
    public bool Engaged => State!=null && State.Engaged;
    public bool Reserved { get; private set; }
    public bool ControlsReleased => Engaged && (State.Current==HuntAmbushState.Phase.Release
        || State.Current==HuntAmbushState.Phase.Recovery || State.Current==HuntAmbushState.Phase.Complete);
    public bool BlocksPlayer => (Engaged && !ControlsReleased) || Time.frameCount<=releasedFrame;
    public float Stress => !Engaged?0f:State.Current==HuntAmbushState.Phase.Recovery?1-State.Progress
        :State.WasBound?.95f:.65f;
    private readonly GameModeController modes;
    private readonly HuntAmbushState.Settings settings;
    private readonly Collider player;
    private readonly PlayerVerticalMovement vertical;
    private readonly ObstacleSpawner spawner;
    private readonly RectTransform curtain;
    private readonly HuntAmbushGraphic graphic;
    private readonly TMP_Text prompt;
    private readonly KeyCode[] keys;
    private readonly HuntAmbushGraphic.AttackSide[] directions=new HuntAmbushGraphic.AttackSide[3];
    private static readonly KeyCode[] Keyboard=(KeyCode[])Enum.GetValues(typeof(KeyCode));
    private int lastHunt=-1,releasedFrame=-1;
    private bool selected,used,ownsTime,ownsPose,focusPaused,skipResumeFrame;
    private float savedScale,savedFixed,entryEdge,returnFrom;
    private Vector3 entryPosition,entryScale;
    private Quaternion entryRotation;
    private HuntAmbushState.Phase previous;

    public HuntAmbush(GameModeController modes,HuntAmbushState.Settings settings,KeyCode[] keys,Collider player,
        ObstacleSpawner spawner,RectTransform curtain,Transform uiRoot,TMP_FontAsset font)
    {
        this.modes=modes;this.settings=settings;this.keys=keys;this.player=player;this.spawner=spawner;this.curtain=curtain;
        vertical=player.GetComponent<PlayerVerticalMovement>();
        var go=new GameObject("Hunt Ambush Tendrils",typeof(RectTransform),typeof(CanvasRenderer),typeof(HuntAmbushGraphic));
        go.transform.SetParent(uiRoot,false);graphic=go.GetComponent<HuntAmbushGraphic>();graphic.raycastTarget=false;graphic.Curtain=curtain.GetComponent<ViscousCurtain>();
        var r=graphic.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        var label=new GameObject("Ambush Key",typeof(RectTransform),typeof(TextMeshProUGUI));label.transform.SetParent(uiRoot,false);
        prompt=label.GetComponent<TextMeshProUGUI>();if(font!=null)prompt.font=font;
        prompt.fontStyle=FontStyles.Bold;prompt.fontSize=28;prompt.alignment=TextAlignmentOptions.Center;
        prompt.color=Color.white;prompt.outlineWidth=.3f;prompt.outlineColor=Color.black;prompt.raycastTarget=false;
        prompt.rectTransform.sizeDelta=new Vector2(150,55);prompt.gameObject.SetActive(false);
    }
    public void Reset(){Cancel();lastHunt=-1;used=selected=false;releasedFrame=-1;}
    public bool Prepare(TentacleAttackState hunt,double meters,float huntLength)
    {
        if(Engaged)return true;
        Reserved=false;
        if(!settings.enabled || hunt==null || hunt.CurrentStage!=TentacleAttackState.Stage.Hunt || Time.timeScale<=0)return false;
        if(lastHunt!=hunt.CompletedHunts)
        {lastHunt=hunt.CompletedHunts;used=false;selected=UnityEngine.Random.value<Mathf.Clamp01(settings.chancePerHunt);}
        if(used || !selected || meters-hunt.HuntStartMeters<huntLength*Mathf.Clamp(settings.huntProgress,.2f,.9f))return false;
        bool idle=hunt.CurrentAction==TentacleAttackState.Action.Idle || hunt.CurrentAction==TentacleAttackState.Action.Waiting;
        if(!idle)return false; // Never interrupt an announced or committed strike.
        Reserved=true;
        if(vertical==null || !vertical.IsFullyStanding)return true;
        if(spawner!=null && spawner.HasPendingObstacle(player.bounds.min.x))return true;
        if(Camera.main==null || !modes.CanAffordCrouch(0,0,0) || modes.CurtainDanger(.01f)>=1f)
        {used=true;Reserved=false;return false;}
        Begin();return true;
    }
    private void Begin()
    {
        used=true;Reserved=true;ownsPose=true;releasedFrame=-1;
        State=new HuntAmbushState(settings);
        int a=UnityEngine.Random.Range(0,3),b=(a+UnityEngine.Random.Range(1,3))%3,c=3-a-b;
        State.Begin(a,b,c);previous=State.Current;
        entryPosition=player.transform.position;entryScale=player.transform.localScale;entryRotation=player.transform.localRotation;
        entryEdge=returnFrom=curtain.anchorMax.x;
        Rect bounds=CurtainTentacles.ViewportBounds(Camera.main,player.bounds);
        float aspect=graphic.rectTransform.rect.height/Mathf.Max(1,graphic.rectTransform.rect.width);
        bool leftFits=bounds.xMin-entryEdge>(State.Configuration.reactionRingRadius*2+.045f)*aspect;
        for(int i=0;i<3;i++)
        {
            int next=UnityEngine.Random.Range(leftFits?0:1,3);
            if(i>0 && next==(int)directions[i-1])next=leftFits?(next+UnityEngine.Random.Range(1,3))%3:next==1?2:1;
            directions[i]=(HuntAmbushGraphic.AttackSide)next;
        }
        savedScale=Time.timeScale;savedFixed=Time.fixedDeltaTime;ownsTime=true;
        ApplyTime();Render();
    }
    public void Tick(float realSeconds)
    {
        if(!Engaged)return;
        if(!settings.enabled){Cancel();return;}
        if(Time.timeScale<=0 || focusPaused)return;
        if(skipResumeFrame){skipResumeFrame=false;return;}
        ReadInput(out int key,out bool space);
        Step(realSeconds,key,space);
    }
    // Uses key-down edges, shared by live play and integration validation.
    public void Step(float realSeconds,int key=-1,bool space=false)
    {
        if(!Engaged || realSeconds<=0)return;
        State.Tick(realSeconds,key,space);
        if(State.Current==HuntAmbushState.Phase.Release && previous!=State.Current)
        {
            returnFrom=curtain.anchorMax.x;
            RestorePose();
            // Consume this frame's escape key edge only. Movement and sprint
            // resume next frame while the tendrils retract and respite continues.
            releasedFrame=Time.frameCount;
        }
        previous=State.Current;
        ApplyTime();Render();
    }
    private void ReadInput(out int key,out bool space)
    {
        key=-1;space=Input.GetKeyDown(KeyCode.Space);
        if(!Input.anyKeyDown)return;
        int count=0;
        foreach(KeyCode code in Keyboard)
        {
            if(code==KeyCode.None || code==KeyCode.Escape || (int)code>=(int)KeyCode.Mouse0)continue;
            if(code==KeyCode.LeftShift || code==KeyCode.RightShift || code==KeyCode.LeftControl || code==KeyCode.RightControl
                || code==KeyCode.LeftAlt || code==KeyCode.RightAlt || code==KeyCode.LeftCommand || code==KeyCode.RightCommand)continue;
            if(!Input.GetKeyDown(code))continue;
            count++;int match=Array.IndexOf(keys,code);key=match>=0?match:-2;
        }
        if(count>1)key=-2;
    }
    private void ApplyTime()
    {
        if(!ownsTime || focusPaused)return;
        Time.timeScale=savedScale*State.TimeScale;
        Time.fixedDeltaTime=savedFixed*State.TimeScale;
    }
    private void Render()
    {
        if(!Engaged || Camera.main==null)return;
        var phase=State.Current;
        var direction=directions[State.Step];
        float dodge=phase==HuntAmbushState.Phase.Evade?Mathf.Sin(State.Progress*Mathf.PI):0f;
        float dodgeSign=direction==HuntAmbushGraphic.AttackSide.Right?-1:1;
        if(ownsPose)player.transform.position=entryPosition+Vector3.right*(dodge*.12f*dodgeSign);
        if(ownsPose)player.transform.localRotation=entryRotation*Quaternion.Euler(0,0,dodge*-10*dodgeSign);
        Physics.SyncTransforms();
        Rect bounds=CurtainTentacles.ViewportBounds(Camera.main,player.bounds);
        float edge=entryEdge;
        float near=Mathf.Max(entryEdge,bounds.xMin-.003f);
        if(phase==HuntAmbushState.Phase.Bind)edge=Mathf.Lerp(entryEdge,near,.45f*Mathf.SmoothStep(0,1,State.Progress));
        else if(phase==HuntAmbushState.Phase.Struggle)
            edge=Mathf.Lerp(entryEdge,near,Mathf.Clamp01(.45f+.5f*State.Progress-.28f*State.EscapeProgress));
        else if(phase==HuntAmbushState.Phase.Consume || phase==HuntAmbushState.Phase.Dead)
            edge=Mathf.Lerp(near,bounds.xMax+.01f,phase==HuntAmbushState.Phase.Dead?1:Mathf.SmoothStep(0,1,State.Progress));
        else if(phase==HuntAmbushState.Phase.Release)edge=Mathf.Lerp(returnFrom,entryEdge,Mathf.SmoothStep(0,1,State.Progress));
        curtain.anchorMax=new Vector2(edge,1);
        bool struggle=phase==HuntAmbushState.Phase.Bind || phase==HuntAmbushState.Phase.Struggle;
        bool keyVisible=struggle || phase==HuntAmbushState.Phase.Focus || phase==HuntAmbushState.Phase.Response;
        float aspect=graphic.rectTransform.rect.height/Mathf.Max(1,graphic.rectTransform.rect.width);
        HuntAmbushGraphic.StrikePath(bounds,edge,direction,State.Step,aspect,State.Configuration.reactionRingRadius,
            out _,out _,out _,out _,out Vector2 keyPosition);
        if(struggle)keyPosition=new Vector2(Mathf.Clamp(bounds.xMax+.09f,.12f,.86f),Mathf.Clamp(bounds.yMax+.075f,.16f,.87f));
        prompt.gameObject.SetActive(keyVisible);
        if(keyVisible)
        {
            prompt.text=struggle?"SPACE":KeyLabel(keys[State.RequiredKey]);
            prompt.rectTransform.anchorMin=prompt.rectTransform.anchorMax=keyPosition;
            prompt.rectTransform.anchoredPosition=Vector2.zero;
            prompt.rectTransform.localScale=Vector3.one*HuntAmbushGraphic.KeyScale(State);
            prompt.fontSize=struggle?20:Mathf.Clamp(State.Configuration.reactionRingRadius*graphic.rectTransform.rect.height*1.3f,18f,42f);
        }
        graphic.Show(State,bounds,edge,keyPosition,keyVisible,struggle,direction);
    }
    private static string KeyLabel(KeyCode code)
    {string value=code.ToString();return value.StartsWith("Alpha")?value.Substring(5):value.ToUpperInvariant();}
    public void FocusChanged(bool focused)
    {
        if(!Engaged)return;
        if(!focused && Time.timeScale>0){focusPaused=true;Time.timeScale=0;}
        else if(focused && focusPaused){focusPaused=false;skipResumeFrame=true;ApplyTime();}
    }
    private void RestorePose()
    {
        if(!ownsPose)return;
        if(player!=null){player.transform.position=entryPosition;player.transform.localScale=entryScale;player.transform.localRotation=entryRotation;}
        if(vertical!=null)vertical.RestoreAfterAmbush();
        ownsPose=false;
    }
    public void Cancel(bool restoreCurtain=true)
    {
        if(Engaged)
        {
            bool wasLocked=ownsPose;RestorePose();

            if(restoreCurtain && curtain!=null)curtain.anchorMax=new Vector2(entryEdge,1);
            if(wasLocked)releasedFrame=Time.frameCount;State.Cancel();
        }
        if(ownsTime){Time.timeScale=savedScale;Time.fixedDeltaTime=savedFixed;ownsTime=false;}
        Reserved=focusPaused=skipResumeFrame=false;
        if(graphic!=null)graphic.Hide();if(prompt!=null)prompt.gameObject.SetActive(false);
    }
}
