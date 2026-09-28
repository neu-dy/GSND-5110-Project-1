using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>One-click setup for the first ClazyRunner gameplay animation pass.</summary>
public static class ClazyRunnerAnimationSetup
{
    private const string Pack = "Assets/CLazyRunnerActionAnimPack/Animations/";
    private const string ModelPath = "Assets/CLazyRunnerActionAnimPack/Models/ClazyRunner.FBX";
    private const string ControllerPath = "Assets/CLazyRunnerActionAnimPack/ClazyRunnerGameplay.controller";
    private const float ModelScale = .5f;

    [MenuItem("Tools/ClazyRunner/Update Gameplay Animations %#F8")]
    private static void UpdateGameplayAnimations()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) return;
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            if (child.state.name == "Idle") child.state.motion = Clip("P1_CLazyMovement/Mvm_Idle/CLazy@Idle_Wait_B.FBX");
            if (child.state.name == "Run") child.state.motion = RunBlend(controller, child.state.motion as BlendTree);
            EditorUtility.SetDirty(child.state);
        }
        ConfigureDashAnimation(controller);
        ConfigureGrabAnimation(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static void ConfigureGrabAnimation(AnimatorController controller)
    {
        bool parameterFound = false;
        foreach (AnimatorControllerParameter parameter in controller.parameters)
            if (parameter.name == "Grabbed") parameterFound = true;
        if (!parameterFound) controller.AddParameter("Grabbed", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState grabbed = null, run = null, idle = null;
        foreach (ChildAnimatorState child in sm.states)
        {
            if (child.state.name == "Grabbed") grabbed = child.state;
            if (child.state.name == "Run") run = child.state;
            if (child.state.name == "Idle") idle = child.state;
        }
        if (grabbed == null) grabbed = sm.AddState("Grabbed");
        grabbed.motion = Clip("P6_CLazyWallRun/WallRun_Hold/CLazy@Wall_RHold.FBX");
        bool entryFound = false;
        foreach (AnimatorStateTransition transition in sm.anyStateTransitions)
        {
            if (transition.destinationState == grabbed) { entryFound = true; continue; }
            bool guardFound = false;
            foreach (AnimatorCondition condition in transition.conditions)
                if (condition.parameter == "Grabbed" && condition.mode == AnimatorConditionMode.IfNot) guardFound = true;
            if (!guardFound) transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grabbed");
            EditorUtility.SetDirty(transition);
        }
        if (!entryFound) AnyTransition(sm, grabbed, "Grabbed", AnimatorConditionMode.If);
        foreach (AnimatorState target in new[] { run, idle })
        {
            if (target == null) continue;
            bool exitFound = false;
            foreach (AnimatorStateTransition transition in grabbed.transitions)
                if (transition.destinationState == target) exitFound = true;
            if (exitFound) continue;
            Transition(grabbed, target, false, "Grabbed", AnimatorConditionMode.IfNot);
            AnimatorStateTransition exit = grabbed.transitions[grabbed.transitions.Length - 1];
            exit.AddCondition(target == run ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "Running");
        }
        EditorUtility.SetDirty(grabbed);
        EditorUtility.SetDirty(sm);
    }

    private static void ConfigureDashAnimation(AnimatorController controller)
    {
        bool parameterFound = false;
        foreach (AnimatorControllerParameter parameter in controller.parameters)
            if (parameter.name == "Dashing") parameterFound = true;
        if (!parameterFound) controller.AddParameter("Dashing", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState dash = null, run = null, slide = null;
        foreach (ChildAnimatorState child in sm.states)
        {
            if (child.state.name == "Burst Dash") dash = child.state;
            if (child.state.name == "Run") run = child.state;
            if (child.state.name == "Slide Start") slide = child.state;
        }
        if (dash == null) dash = sm.AddState("Burst Dash");
        dash.motion = Clip("P1_CLazyMovement/Mvm_8way_ChargeBoost/CLazy@Mvm_ChargeBoost.FBX");
        if (run != null && !HasTransition(run, dash))
            Transition(run, dash, false, "Dashing", AnimatorConditionMode.If);
        if (run != null && !HasTransition(dash, run))
            Transition(dash, run, false, "Dashing", AnimatorConditionMode.IfNot);
        if (slide != null && !HasTransition(dash, slide))
            Transition(dash, slide, false, "Ducking", AnimatorConditionMode.If);
        EditorUtility.SetDirty(dash);
        EditorUtility.SetDirty(sm);
    }

    private static bool HasTransition(AnimatorState from, AnimatorState to)
    {
        foreach (AnimatorStateTransition transition in from.transitions)
            if (transition.destinationState == to) return true;
        return false;
    }

    private static BlendTree RunBlend(AnimatorController controller, BlendTree existing = null)
    {
        bool parameterFound = false;
        foreach (AnimatorControllerParameter parameter in controller.parameters)
            if (parameter.name == "Sprint") parameterFound = true;
        if (!parameterFound) controller.AddParameter("Sprint", AnimatorControllerParameterType.Float);
        BlendTree tree = existing != null ? existing : new BlendTree { name = "Jog to Sprint" };
        if (existing == null) AssetDatabase.AddObjectToAsset(tree, controller);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Sprint";
        tree.useAutomaticThresholds = false;
        tree.children = new[] {
            new ChildMotion { motion = Clip("P1_CLazyMovement/Mvm_8way_Jog/CLazy@Mvm_Jog.FBX"), threshold = 0f, timeScale = 1f },
            new ChildMotion { motion = Clip("P1_CLazyMovement/Mvm_8way_Dash/CLazy@Mvm_Dash.FBX"), threshold = 1f, timeScale = 1f }
        };
        EditorUtility.SetDirty(tree);
        return tree;
    }
    static ClazyRunnerAnimationSetup()
    {
        // The requested gameplay scene is already open during this script import. Apply once
        // after Unity finishes its domain reload so the user does not need a second setup step.
        EditorApplication.delayCall += AutoSetupOpenGameplayScene;
    }

    [MenuItem("Tools/ClazyRunner/Setup Side-Scroller Animations _F6")]
    private static void Setup()
    {
        SetupCharacter(true);
    }

    [MenuItem("Tools/ClazyRunner/Fit Character to Player _F7")]
    private static void FitCharacterToPlayer()
    {
        PlayerVerticalMovement movement = UnityEngine.Object.FindAnyObjectByType<PlayerVerticalMovement>();
        Transform visual = movement != null ? movement.transform.Find("ClazyRunner Visual") : null;
        Transform model = visual != null && visual.childCount > 0 ? visual.GetChild(0) : null;
        if (model == null)
        {
            EditorUtility.DisplayDialog("ClazyRunner setup", "Set up the character first with F6.", "OK");
            return;
        }
        FitVisual(movement, visual, model);
        EditorSceneManager.MarkSceneDirty(movement.gameObject.scene);
        EditorSceneManager.SaveScene(movement.gameObject.scene);
    }

    private static void FitVisual(PlayerVerticalMovement movement, Transform visual, Transform model)
    {
        BoxCollider hitbox = movement.GetComponent<BoxCollider>();
        Animator animator = model.GetComponent<Animator>();
        if (hitbox == null || animator == null) return;
        // Measure a disposable, uniformly scaled rig in the actual idle pose. Renderer bounds
        // include animation padding and cannot be used as the character's visible shoe height.
        GameObject reference = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        reference.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            reference.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
            reference.transform.localScale = Vector3.one;
            Animator referenceAnimator = reference.GetComponent<Animator>();
            referenceAnimator.runtimeAnimatorController = animator.runtimeAnimatorController;
            referenceAnimator.applyRootMotion = false;
            referenceAnimator.Rebind();
            referenceAnimator.Update(0f);
            Bounds body = VisibleBounds(reference);
            float scale = hitbox.bounds.size.y / Mathf.Max(.001f, body.size.y);
            float lowestFoot = float.PositiveInfinity;
            foreach (HumanBodyBones bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                         HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            {
                Transform foot = referenceAnimator.GetBoneTransform(bone);
                if (foot != null) lowestFoot = Mathf.Min(lowestFoot, foot.position.y);
            }
            Undo.RecordObjects(new UnityEngine.Object[] { visual, model, animator }, "Ground and fit ClazyRunner");
            Vector3 parentScale = movement.transform.lossyScale;
            visual.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);
            visual.localPosition = hitbox.center - Vector3.up * (hitbox.size.y * .5f);
            model.localRotation = Quaternion.Euler(0f, 90f, 0f);
            model.localScale = Vector3.one * scale;
            model.localPosition = new Vector3(0f, -body.min.y * scale, 0f);
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            var driver = new SerializedObject(movement.GetComponent<ClazyPlayerAnimationDriver>());
            driver.FindProperty("soleOffset").floatValue = float.IsPositiveInfinity(lowestFoot)
                ? .03f : Mathf.Max(0f, (lowestFoot - body.min.y) * scale);
            driver.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(model);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        }
        finally { UnityEngine.Object.DestroyImmediate(reference); }
    }

    private static Bounds VisibleBounds(GameObject model)
    {
        Bounds bounds = default;
        bool initialized = false;
        var mesh = new Mesh();
        try
        {
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.BakeMesh(mesh, false);
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = renderer.transform.TransformPoint(vertex);
                    if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(point);
                }
                mesh.Clear();
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
        if (!initialized) throw new InvalidOperationException("No visible character mesh found.");
        return bounds;
    }

    private static void AutoSetupOpenGameplayScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/DodgeMechTest.unity") return;
        SetupCharacter(false);
    }

    private static void SetupCharacter(bool showDialog)
    {
        PlayerVerticalMovement movement = UnityEngine.Object.FindAnyObjectByType<PlayerVerticalMovement>();
        if (movement == null)
        {
            if (showDialog) EditorUtility.DisplayDialog("ClazyRunner setup", "Open the gameplay scene with the Player object first.", "OK");
            return;
        }

        ClazyPlayerAnimationDriver existing = movement.GetComponent<ClazyPlayerAnimationDriver>();
        if (existing != null)
        {
            if (showDialog) EditorUtility.DisplayDialog("ClazyRunner setup", "The character animation setup is already present on this Player.", "OK");
            return;
        }

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
        {
            if (showDialog) EditorUtility.DisplayDialog("ClazyRunner setup", "Could not find the ClazyRunner model at:\n" + ModelPath, "OK");
            return;
        }
        Avatar avatar = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            if (asset is Avatar found && found.isHuman) { avatar = found; break; }
        if (avatar == null)
        {
            if (showDialog) EditorUtility.DisplayDialog("ClazyRunner setup", "The imported model does not contain a Humanoid Avatar.", "OK");
            return;
        }

        AnimatorController controller;
        try { controller = BuildController(); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (showDialog) EditorUtility.DisplayDialog("ClazyRunner setup", exception.Message, "OK");
            return;
        }
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set up ClazyRunner animations");
        GameObject player = movement.gameObject;
        Undo.RecordObject(player, "Hide placeholder mesh");
        foreach (MeshRenderer renderer in player.GetComponents<MeshRenderer>())
            Undo.RecordObject(renderer, "Hide placeholder mesh");
        foreach (MeshRenderer renderer in player.GetComponents<MeshRenderer>()) renderer.enabled = false;

        var visual = new GameObject("ClazyRunner Visual");
        Undo.RegisterCreatedObjectUndo(visual, "Create character visual root");
        visual.transform.SetParent(player.transform, false);
        visual.transform.localPosition = new Vector3(0f, -.5f, 0f);
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, player.scene);
        if (instance == null) instance = UnityEngine.Object.Instantiate(modelAsset);
        Undo.RegisterCreatedObjectUndo(instance, "Add ClazyRunner model");
        instance.name = "ClazyRunner Model";
        instance.transform.SetParent(visual.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // Face screen-right in the 2D side view.
        instance.transform.localScale = Vector3.one * ModelScale;

        Animator animator = instance.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(instance);
        Undo.RecordObject(animator, "Configure ClazyRunner Animator");
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false; // The existing scripts own all game movement.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        ClazyPlayerAnimationDriver driver = Undo.AddComponent<ClazyPlayerAnimationDriver>(player);
        var serialized = new SerializedObject(driver);
        serialized.FindProperty("animator").objectReferenceValue = animator;
        serialized.FindProperty("vertical").objectReferenceValue = movement;
        serialized.FindProperty("modes").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<GameModeController>();
        serialized.FindProperty("visualRoot").objectReferenceValue = visual.transform;
        serialized.ApplyModifiedProperties();

        FitVisual(movement, visual.transform, instance.transform);

        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(movement.gameObject.scene);
        EditorSceneManager.SaveScene(movement.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = player;
        if (showDialog) EditorUtility.DisplayDialog("ClazyRunner setup", "Side-view running, jump/landing, and hold-to-slide animations are ready. The original movement and hitbox stay on Player.", "OK");
    }

    private static AnimatorController BuildController()
    {
        AnimationClip idle = Clip("P1_CLazyMovement/Mvm_Idle/CLazy@Idle_Wait_B.FBX");
        AnimationClip jumpStart = Clip("P3_CLazyJump/Jmp_Base/CLazy@Jmp_Base_A_Start.FBX");
        AnimationClip jumpUp = Clip("P3_CLazyJump/Jmp_Air_Loop/CLazy@Jump_Up_A_Loop.FBX");
        AnimationClip jumpDown = Clip("P3_CLazyJump/Jmp_Air_Loop/CLazy@Jump_Down_A_Loop.FBX");
        AnimationClip land = Clip("P3_CLazyJump/Jmp_Land/CLazy@Land_Base_Move.FBX");
        AnimationClip slideStart = Clip("P2_CLazyEscape/Esc_Slide_Fwd/CLazy@Esc_Slide_Start.FBX");
        AnimationClip slideLoop = Clip("P2_CLazyEscape/Esc_Slide_Loop/CLazy@Esc_Slide_Loop.FBX");
        AnimationClip slideEnd = Clip("P2_CLazyEscape/Esc_Slide_Fwd/CLazy@Esc_Slide_End.FBX");

        EnsureAssetFolder("Assets/CLazyRunnerActionAnimPack");
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Running", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Ducking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("VerticalVelocity", AnimatorControllerParameterType.Float);
        controller.AddParameter("Land", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idleState = sm.AddState("Idle"); idleState.motion = idle;
        AnimatorState runState = sm.AddState("Run"); runState.motion = RunBlend(controller);
        AnimatorState slideStartState = sm.AddState("Slide Start"); slideStartState.motion = slideStart;
        AnimatorState slideLoopState = sm.AddState("Slide Loop"); slideLoopState.motion = slideLoop;
        AnimatorState slideEndState = sm.AddState("Slide End"); slideEndState.motion = slideEnd;
        AnimatorState jumpStartState = sm.AddState("Jump Start"); jumpStartState.motion = jumpStart;
        AnimatorState jumpUpState = sm.AddState("Jump Up"); jumpUpState.motion = jumpUp;
        AnimatorState jumpDownState = sm.AddState("Jump Down"); jumpDownState.motion = jumpDown;
        AnimatorState landState = sm.AddState("Land"); landState.motion = land;
        sm.defaultState = idleState;

        Transition(idleState, runState, false, "Running", AnimatorConditionMode.If);
        Transition(runState, idleState, false, "Running", AnimatorConditionMode.IfNot);
        Transition(idleState, slideStartState, false, "Ducking", AnimatorConditionMode.If);
        Transition(runState, slideStartState, false, "Ducking", AnimatorConditionMode.If);
        Transition(slideStartState, slideLoopState, true, null, AnimatorConditionMode.If, .78f);
        Transition(slideLoopState, slideEndState, false, "Ducking", AnimatorConditionMode.IfNot);
        Transition(slideEndState, slideStartState, false, "Ducking", AnimatorConditionMode.If);
        Transition(slideEndState, runState, true, null, AnimatorConditionMode.If, .82f);

        AnyTransition(sm, jumpStartState, "Jump", AnimatorConditionMode.If);
        Transition(jumpStartState, jumpUpState, true, null, AnimatorConditionMode.If, .72f);
        Transition(jumpUpState, jumpDownState, false, "VerticalVelocity", AnimatorConditionMode.Less, -.1f);
        AnyTransition(sm, landState, "Land", AnimatorConditionMode.If);
        Transition(landState, slideStartState, true, "Ducking", AnimatorConditionMode.If, .75f);
        Transition(landState, runState, true, "Ducking", AnimatorConditionMode.IfNot, .75f);
        ConfigureDashAnimation(controller);
        ConfigureGrabAnimation(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimationClip Clip(string relativePath)
    {
        string path = Pack + relativePath;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
        throw new InvalidOperationException("Animation clip not found: " + path);
    }

    private static void Transition(AnimatorState from, AnimatorState to, bool exitTime,
        string parameter, AnimatorConditionMode mode, float threshold = 0f)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = exitTime;
        transition.exitTime = threshold;
        transition.duration = .06f;
        transition.hasFixedDuration = true;
        transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        if (parameter != null) transition.AddCondition(mode, threshold, parameter);
    }

    private static void AnyTransition(AnimatorStateMachine sm, AnimatorState to,
        string parameter, AnimatorConditionMode mode)
    {
        AnimatorStateTransition transition = sm.AddAnyStateTransition(to);
        transition.hasExitTime = false;
        transition.duration = .04f;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        transition.AddCondition(mode, 0f, parameter);
    }

    private static void EnsureAssetFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string folder = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }
}
