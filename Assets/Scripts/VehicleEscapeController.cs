using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Owns the stationary finale. Ordinary gameplay never advances during this sequence.
public sealed class VehicleEscapeController : IDisposable
{
    public VehicleEscapeState State { get; }
    public bool Active => !disposed;
    private readonly GameModeController modes;
    private readonly Collider player;
    private readonly PlayerSprint sprint;
    private readonly PlayerVerticalMovement vertical;
    private readonly RectTransform curtain;
    private readonly ViscousCurtain curtainShape;
    private readonly SwallowParticles particles;
    private readonly Camera camera;
    private readonly GameObject vehicle,ui;
    private readonly Material material,detailMaterial;
    private readonly TMP_Text prompt,message,countdown;
    private readonly QteKeyGraphic keyFrame;
    private readonly HuntAmbushState.Settings keyStyle;
    private readonly Renderer[] playerBodies;
    private readonly Collider[] playerColliders;
    private readonly bool[] bodyVisibility,colliderEnabled;
    private readonly Vector3 playerStart,playerScale,door,driverSeat,keyAnchor,vehicleStart,vehicleParked,vehicleEnd;
    private readonly Quaternion playerRotation,cameraRotation;
    private readonly Vector3 cameraPosition;
    private readonly float cameraFov,cameraSize,entryEdge,safeEdge;
    private float catchEdge,segmentEdge,segmentStarted;
    private readonly int originalCurtainOrder;
    private float elapsed,pulse,errorFlash,dustClock;
    private bool disposed,focusPaused,skipResume,failedHandled,returned,covering,boarded;
    private static readonly KeyCode[] Keyboard=(KeyCode[])Enum.GetValues(typeof(KeyCode));

    public VehicleEscapeController(GameModeController modes,VehicleEscapeState.Settings settings,
        Collider player,RectTransform curtain,SwallowParticles particles,TMP_FontAsset font,HuntAmbushState.Settings qteStyle=null)
    {
        this.modes=modes;this.player=player;this.curtain=curtain;this.particles=particles;
        keyStyle=qteStyle??new HuntAmbushState.Settings();
        curtainShape=curtain.GetComponent<ViscousCurtain>();camera=Camera.main;
        sprint=player.GetComponent<PlayerSprint>();vertical=player.GetComponent<PlayerVerticalMovement>();
        if(vertical!=null)vertical.RestoreAfterAmbush();Physics.SyncTransforms();
        playerStart=player.transform.position;playerScale=player.transform.localScale;playerRotation=player.transform.rotation;
        playerBodies=player.GetComponentsInChildren<Renderer>();playerColliders=player.GetComponentsInChildren<Collider>();
        bodyVisibility=new bool[playerBodies.Length];colliderEnabled=new bool[playerColliders.Length];
        for(int i=0;i<playerBodies.Length;i++)bodyVisibility[i]=playerBodies[i].enabled;
        for(int i=0;i<playerColliders.Length;i++)colliderEnabled[i]=playerColliders[i].enabled;
        cameraPosition=camera.transform.position;cameraRotation=camera.transform.rotation;
        cameraFov=camera.fieldOfView;cameraSize=camera.orthographicSize;
        var config=settings.Copy();var sequence=new int[config.lockPresses];
        for(int i=0;i<sequence.Length;i++)
        {
            int next=UnityEngine.Random.Range(0,config.lockKeys.Length-(i>0?1:0));
            if(i>0&&next>=sequence[i-1])next++;sequence[i]=next;
        }
        State=new VehicleEscapeState(config,sequence);
        float depth=camera.WorldToViewportPoint(player.bounds.center).z;
        float left=camera.ViewportToWorldPoint(new Vector3(0,.5f,depth)).x;
        float right=camera.ViewportToWorldPoint(new Vector3(1,.5f,depth)).x;
        float width=(right-left)*.27f,height=player.bounds.size.y*.65f;
        float ground=vertical!=null?vertical.GroundY:player.bounds.min.y;
        float vehicleDepth=Mathf.Max(1f,player.bounds.size.z*1.6f);
        // Forward is +X and up is +Y, so vehicle-left is +Z (away from the camera).
        // A left-hand-drive car is entered through its far-side front door.
        vehicleParked=new Vector3(camera.ViewportToWorldPoint(new Vector3(.48f,.5f,depth)).x,ground+height*.5f,
            playerStart.z);
        vehicleStart=vehicleParked+Vector3.right*(right-left)*.75f;
        vehicleEnd=vehicleParked+Vector3.right*(right-left)*1.1f;
        driverSeat=vehicleParked+new Vector3(width*.23f,0,vehicleDepth*.25f);
        door=new Vector3(driverSeat.x,playerStart.y,vehicleParked.z+vehicleDepth*.5f+player.bounds.extents.z+.12f);
        keyAnchor=door+(player.bounds.center-playerStart)
            +new Vector3(player.bounds.extents.x,player.bounds.size.y*.25f,0);
        vehicle=GameObject.CreatePrimitive(PrimitiveType.Cube);vehicle.name="Escape Vehicle (Prototype)";
        vehicle.GetComponent<Collider>().enabled=false;
        vehicle.transform.localScale=new Vector3(width,height,vehicleDepth);
        var body=vehicle.GetComponent<Renderer>();
        material=new Material(playerBodies.Length>0?playerBodies[0].sharedMaterial:body.sharedMaterial);
        Color paint=new Color(.18f,.32f,.4f);
        if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",paint);
        if(material.HasProperty("_Color"))material.SetColor("_Color",paint);
        body.sharedMaterial=material;
        detailMaterial=new Material(material);
        Color detail=new Color(.48f,.65f,.7f);
        if(detailMaterial.HasProperty("_BaseColor"))detailMaterial.SetColor("_BaseColor",detail);
        if(detailMaterial.HasProperty("_Color"))detailMaterial.SetColor("_Color",detail);
        foreach(int side in new[]{-1,1})
        {
            string front=side>0?"Driver":"Front Passenger";
            string rear=side>0?"Left Rear":"Right Rear";
            Detail(front+" Window",new Vector3(.23f,.24f,side*.512f),new Vector3(.24f,.29f,.012f));
            Detail(front+" Handle",new Vector3(.115f,-.025f,side*.52f),new Vector3(.055f,.022f,.025f));
            Detail(rear+" Window",new Vector3(-.23f,.24f,side*.512f),new Vector3(.24f,.29f,.012f));
            Detail(rear+" Handle",new Vector3(-.345f,-.025f,side*.52f),new Vector3(.055f,.022f,.025f));
        }
        entryEdge=curtain.anchorMax.x;
        float doorX=camera.WorldToViewportPoint(door).x;
        safeEdge=Mathf.Min(entryEdge,Mathf.Max(0,doorX-config.minimumCurtainGap));
        segmentEdge=safeEdge;
        catchEdge=Mathf.Clamp01(doorX+Mathf.Max(.04f,curtainShape.Amplitude+.01f));
        originalCurtainOrder=curtain.GetSiblingIndex();
        ui=new GameObject("Vehicle Escape UI",typeof(RectTransform));ui.transform.SetParent(curtain.parent,false);
        var rect=(RectTransform)ui.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        var frameObject=new GameObject("Vehicle Key Frame",typeof(RectTransform),typeof(CanvasRenderer),typeof(QteKeyGraphic));
        frameObject.transform.SetParent(ui.transform,false);keyFrame=frameObject.GetComponent<QteKeyGraphic>();keyFrame.raycastTarget=false;
        keyFrame.rectTransform.anchorMin=Vector2.zero;keyFrame.rectTransform.anchorMax=Vector2.one;
        keyFrame.rectTransform.offsetMin=keyFrame.rectTransform.offsetMax=Vector2.zero;
        prompt=Label("Vehicle Key",ui.transform,font,28,Color.white,new Vector2(.55f,.65f));
        prompt.enableAutoSizing=false;prompt.rectTransform.sizeDelta=new Vector2(150,55);
        prompt.outlineWidth=.3f;prompt.outlineColor=Color.black;
        message=Label("Temporary Safety",ui.transform,font,34,new Color(.25f,1f,.42f),new Vector2(.57f,.62f));
        message.text="YOU'RE SAFE. FOR NOW...";
        countdown=Label("Return Countdown",curtain.parent,font,24,new Color(.78f,.8f,.81f),new Vector2(.5f,.5f));
        Render(0);
    }
    private void Detail(string name,Vector3 localPosition,Vector3 localScale)
    {
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;
        part.GetComponent<Collider>().enabled=false;part.transform.SetParent(vehicle.transform,false);
        part.transform.localPosition=localPosition;part.transform.localScale=localScale;
        part.GetComponent<Renderer>().sharedMaterial=detailMaterial;
    }
    private static TMP_Text Label(string name,Transform parent,TMP_FontAsset font,int size,Color color,Vector2 anchor)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
        var label=go.GetComponent<TextMeshProUGUI>();if(font!=null)label.font=font;
        label.fontSize=size;label.enableAutoSizing=true;label.fontSizeMin=size*.65f;label.fontSizeMax=size;
        label.fontStyle=FontStyles.Bold;label.alignment=TextAlignmentOptions.Center;label.color=color;label.raycastTarget=false;
        label.outlineWidth=.12f;label.outlineColor=new Color32(10,20,24,255);
        label.rectTransform.anchorMin=label.rectTransform.anchorMax=anchor;
        label.rectTransform.sizeDelta=new Vector2(650,80);label.gameObject.SetActive(false);return label;
    }
    public void Tick(float dt)
    {
        if(disposed||focusPaused||Time.timeScale<=0)return;
        if(skipResume){skipResume=false;return;}
        int key=-1;bool space=false;
        if(State.Current==VehicleEscapeState.Phase.Lockpick&&Input.anyKeyDown)
        {
            int count=0;
            foreach(KeyCode code in Keyboard)
            {
                if(code==KeyCode.None||code==KeyCode.Escape||(int)code>=(int)KeyCode.Mouse0)continue;
                if(code==KeyCode.LeftShift||code==KeyCode.RightShift||code==KeyCode.LeftControl||code==KeyCode.RightControl
                    ||code==KeyCode.LeftAlt||code==KeyCode.RightAlt||code==KeyCode.LeftCommand||code==KeyCode.RightCommand)continue;
                if(!Input.GetKeyDown(code))continue;count++;key=Array.IndexOf(State.Configuration.lockKeys,code);
                if(key<0)key=-2;
            }
            if(count>1)key=-2;
        }
        else if(State.Current==VehicleEscapeState.Phase.Ignition)space=Input.GetKeyDown(KeyCode.Space);
        Step(dt,key,space);
    }
    public void Step(float dt,int key=-1,bool space=false)
    {
        if(disposed||focusPaused||dt<=0)return;
        int mistakes=State.Mistakes,presses=State.LockProgress+State.EngineProgress;
        State.Tick(dt,key,space);elapsed+=dt;
        pulse=Mathf.Max(0,pulse-dt*6);errorFlash=Mathf.Max(0,errorFlash-dt);
        if(presses!=State.LockProgress+State.EngineProgress||mistakes!=State.Mistakes)pulse=1;
        if(mistakes!=State.Mistakes)errorFlash=.3f;
        Render(dt);
        if(sprint!=null)sprint.TickStationaryHeartbeat(dt,Danger(.22f));
        if(State.Current==VehicleEscapeState.Phase.Failed&&!failedHandled)
        {
            failedHandled=true;RestorePlayerAtDoor();
            if(boarded)
            {
                player.transform.position=driverSeat;
                foreach(var body in playerBodies)if(body!=null)body.enabled=false;
            }
            Physics.SyncTransforms();prompt.gameObject.SetActive(false);keyFrame.Hide();modes.FailVehicleEscape();
        }
        if(State.Current==VehicleEscapeState.Phase.Complete&&!returned){returned=true;modes.ReturnToMenu();}
    }
    public float Danger(float calmGap)
    {
        if(disposed||camera==null)return 0;
        Vector3 point=camera.WorldToViewportPoint(player.transform.position);
        return 1-Mathf.Clamp01((point.x-curtainShape.EdgeAt(point.y))/Mathf.Max(.01f,calmGap));
    }
    private void Render(float dt)
    {
        var phase=State.Current;var config=State.Configuration;float p=State.Progress;
        bool arrived=phase!=VehicleEscapeState.Phase.Arrival;
        float entrance=Mathf.SmoothStep(0,1,arrived?1:p);
        vehicle.transform.position=Vector3.Lerp(vehicleStart,vehicleParked,entrance);
        vehicle.transform.rotation=Quaternion.identity;
        player.transform.position=Vector3.Lerp(playerStart,door,entrance);
        player.transform.localScale=playerScale;player.transform.rotation=playerRotation;
        if(!boarded&&(int)phase>=(int)VehicleEscapeState.Phase.Boarding&&phase!=VehicleEscapeState.Phase.Failed)
        {
            // Rebase the remaining approach continuously when the player gets inside.
            // A failed ignition is swallowed in the vehicle, never teleported outside.
            boarded=true;segmentEdge=curtain.anchorMax.x;segmentStarted=State.DeadlineElapsed;
            catchEdge=Mathf.Clamp01(camera.WorldToViewportPoint(driverSeat).x+Mathf.Max(.04f,curtainShape.Amplitude+.01f));
        }
        float approach=Mathf.Clamp01((State.DeadlineElapsed-segmentStarted)/Mathf.Max(.001f,config.deadlineSeconds-segmentStarted));
        float front=arrived?Mathf.Lerp(segmentEdge,catchEdge,Mathf.Pow(approach,1.15f))
            :Mathf.Lerp(entryEdge,safeEdge,entrance);
        if(phase==VehicleEscapeState.Phase.Boarding)
        {
            player.transform.position=Vector3.Lerp(door,driverSeat,Mathf.SmoothStep(0,1,p));
            player.transform.localScale=playerScale*Mathf.Lerp(1,.35f,p);
        }
        bool inside=((int)phase>=(int)VehicleEscapeState.Phase.Ignition&&phase!=VehicleEscapeState.Phase.Failed)
            ||(phase==VehicleEscapeState.Phase.Failed&&boarded);
        foreach(var collider in playerColliders)if(collider!=null)collider.enabled=false;
        for(int i=0;i<playerBodies.Length;i++)if(playerBodies[i]!=null)playerBodies[i].enabled=bodyVisibility[i]&&!inside;
        if(inside)player.transform.position=driverSeat;
        if(phase==VehicleEscapeState.Phase.Lockpick||phase==VehicleEscapeState.Phase.Ignition)
        {
            float vibration=pulse*Mathf.Sin(elapsed*65);
            vehicle.transform.position+=Vector3.up*(vibration*.025f);
            vehicle.transform.rotation=Quaternion.Euler(0,vibration*1.5f,vibration*1.2f);
        }
        if(phase==VehicleEscapeState.Phase.Departure)
        {
            // A short load-up, broad tail swing, then accelerating exit on a fixed camera.
            float drive=Mathf.Clamp01((p-.12f)/.88f);
            vehicle.transform.position=Vector3.Lerp(vehicleParked,vehicleEnd,drive*drive)
                +new Vector3(-.12f*Mathf.Sin(Mathf.Min(1,p/.2f)*Mathf.PI),0,Mathf.Sin(drive*Mathf.PI)*.35f);
            vehicle.transform.rotation=Quaternion.Euler(0,-28*Mathf.Sin(drive*Mathf.PI*1.25f)*(1-drive),
                5*Mathf.Sin(drive*Mathf.PI*2)*(1-drive));
            player.transform.position=vehicle.transform.TransformPoint(new Vector3(.23f,0,.25f));
            dustClock+=dt;
            if(dt>0&&dustClock>=.075f&&drive<.7f&&particles!=null)
            {
                dustClock=0;Vector3 rear=vehicle.transform.position-Vector3.right*vehicle.transform.localScale.x*.4f;
                rear.y=vertical!=null?vertical.GroundY:door.y-1;
                Vector3 view=camera.WorldToViewportPoint(rear);
                particles.Burst(view,new Color(.52f,.55f,.55f),3,.65f,false,new Vector2(.035f,.012f));
            }
        }
        else if((int)phase>(int)VehicleEscapeState.Phase.Departure&&phase!=VehicleEscapeState.Phase.Failed)
        {vehicle.transform.position=vehicleEnd;player.transform.position=vehicle.transform.TransformPoint(new Vector3(.23f,0,.25f));}
        bool surge=(int)phase>=(int)VehicleEscapeState.Phase.Surge&&phase!=VehicleEscapeState.Phase.Failed;
        if(surge)
        {
            if(!covering){covering=true;curtain.SetAsLastSibling();}
            front=phase==VehicleEscapeState.Phase.Surge?Mathf.Lerp(front,1,p*p):1;
        }
        curtain.anchorMax=new Vector2(front,1);curtainShape.ForwardLimit=front;curtainShape.RefreshEdge();
        bool lockpick=phase==VehicleEscapeState.Phase.Lockpick,ignition=phase==VehicleEscapeState.Phase.Ignition;
        prompt.gameObject.SetActive(lockpick||ignition);
        if(lockpick||ignition)
        {
            prompt.text=lockpick?config.lockKeys[State.RequiredKey].ToString().ToUpperInvariant():"SPACE";
            prompt.color=errorFlash>0?new Color(1,.2f,.15f):Color.white;
            float scale=QteKeyGraphic.PulseScale(ignition,State.PhaseSeconds,pulse,keyStyle.strugglePulseHz,keyStyle.strugglePulseAmount);
            prompt.rectTransform.localScale=Vector3.one*scale;
            Vector3 beside=camera.WorldToViewportPoint(keyAnchor);
            Rect frameRect=keyFrame.rectTransform.rect;
            float aspect=Mathf.Max(.1f,frameRect.width/Mathf.Max(1,frameRect.height));
            // Reserve space for the pulsing SPACE frame, keeping both prompts beside the same shoulder.
            float maxScale=1+keyStyle.strugglePulseAmount+.12f;
            float halfWidth=Mathf.Max(keyStyle.reactionRingRadius,.033f*2.5f)*maxScale/aspect;
            Vector2 keyPosition=new Vector2(Mathf.Clamp(beside.x+halfWidth+.018f,halfWidth+.02f,1-halfWidth-.02f),
                Mathf.Clamp(beside.y,.2f,.84f));
            prompt.rectTransform.anchorMin=prompt.rectTransform.anchorMax=keyPosition;
            prompt.rectTransform.anchoredPosition=Vector2.zero;
            prompt.fontSize=ignition?20:Mathf.Clamp(keyStyle.reactionRingRadius*keyFrame.rectTransform.rect.height*1.3f,18,42);
            // The ring represents the shared deadline and never refills on a key.
            keyFrame.Show(keyPosition,ignition,keyStyle.reactionRingRadius,1-State.DeadlineProgress,scale,prompt.color);
        }
        else keyFrame.Hide();
        message.gameObject.SetActive(phase==VehicleEscapeState.Phase.SafeMessage||phase==VehicleEscapeState.Phase.Surge);
        countdown.gameObject.SetActive(phase==VehicleEscapeState.Phase.Countdown);
        if(phase==VehicleEscapeState.Phase.Countdown)
        {countdown.transform.SetAsLastSibling();countdown.text="Main menu in "+Mathf.CeilToInt(config.returnCountdownSeconds-State.PhaseSeconds)+"s";}
    }
    private void RestorePlayerAtDoor()
    {
        if(player==null)return;player.transform.position=door;player.transform.localScale=playerScale;player.transform.rotation=playerRotation;
        for(int i=0;i<playerBodies.Length;i++)if(playerBodies[i]!=null)playerBodies[i].enabled=bodyVisibility[i];
        for(int i=0;i<playerColliders.Length;i++)if(playerColliders[i]!=null)playerColliders[i].enabled=colliderEnabled[i];
    }
    public void LateUpdate()
    {
        if(disposed||camera==null)return;
        camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);camera.fieldOfView=cameraFov;camera.orthographicSize=cameraSize;
    }
    public void FocusChanged(bool focused){focusPaused=!focused;if(focused)skipResume=true;}
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(player!=null&&!modes.HasLost)RestorePlayerAtDoor();
        if(curtain!=null)curtain.SetSiblingIndex(originalCurtainOrder);
        if(vehicle!=null)UnityEngine.Object.Destroy(vehicle);
        if(material!=null)UnityEngine.Object.Destroy(material);
        if(detailMaterial!=null)UnityEngine.Object.Destroy(detailMaterial);
        if(ui!=null)UnityEngine.Object.Destroy(ui);
        if(countdown!=null)UnityEngine.Object.Destroy(countdown.gameObject);
    }
}
