using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class OrcAnimatorControllerSetup
{
    private const string ControllerPath = "Assets/FEFE/Animations/OrcAnimations.controller";

    [MenuItem("Window/FEFE/Setup Orc Animator Controller")]
    public static void SetupController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[OrcAnimatorControllerSetup] Could not find controller at {ControllerPath}");
            return;
        }

        EnsureParameter(controller, "Speed", AnimatorControllerParameterType.Float);
        EnsureParameter(controller, "Dead", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "CombatState", AnimatorControllerParameterType.Int);
        EnsureParameter(controller, "AttackIndex", AnimatorControllerParameterType.Float);
        EnsureParameter(controller, "Attack", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Block", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "Parry", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Stagger", AnimatorControllerParameterType.Trigger);

        var layer = controller.layers[0];
        var sm = layer.stateMachine;

        var locomotion = FindState(sm, "Locomtion") ?? FindState(sm, "Locomotion") ?? FindState(sm, "Idle");
        var existingAttack = FindState(sm, "Attack");
        var attackClip = existingAttack != null ? existingAttack.motion : null;

        if (existingAttack == null)
            existingAttack = sm.AddState("Attack", new Vector3(360f, 40f, 0f));

        existingAttack.motion = CreateOrReuseAttackTree(controller, attackClip);
        existingAttack.writeDefaultValues = false;

        var block = EnsureState(sm, "Block", new Vector3(360f, 150f, 0f));
        var parry = EnsureState(sm, "Parry", new Vector3(560f, 40f, 0f));
        var stagger = EnsureState(sm, "Stagger", new Vector3(560f, 150f, 0f));
        var dead = EnsureState(sm, "Dead", new Vector3(60f, 230f, 0f));

        block.writeDefaultValues = false;
        parry.writeDefaultValues = false;
        stagger.writeDefaultValues = false;
        dead.writeDefaultValues = false;

        EnsureAnyStateTriggerTransition(sm, existingAttack, "Attack", 0.05f);
        EnsureAnyStateBoolTransition(sm, block, "Block", true, 0.05f);
        EnsureAnyStateTriggerTransition(sm, parry, "Parry", 0.05f);
        EnsureAnyStateTriggerTransition(sm, stagger, "Stagger", 0.05f);
        EnsureAnyStateBoolTransition(sm, dead, "Dead", true, 0.1f);

        if (locomotion != null)
        {
            EnsureExitTransition(existingAttack, locomotion, 0.9f, 0.1f);
            EnsureExitTransition(parry, locomotion, 0.9f, 0.1f);
            EnsureExitTransition(stagger, locomotion, 0.9f, 0.1f);
            EnsureBlockExitTransition(block, locomotion);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[OrcAnimatorControllerSetup] Orc animator controller setup complete. Assign Block/Parry/Stagger clips manually if their states are empty.");
    }

    private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        var parameters = controller.parameters;
        for (int i = parameters.Length - 1; i >= 0; i--)
        {
            var p = parameters[i];
            if (p.name == name)
            {
                if (p.type == type)
                    return;

                controller.RemoveParameter(i);
                Debug.Log($"[OrcAnimatorControllerSetup] Recreated parameter '{name}' as {type}.");
                controller.AddParameter(name, type);
                return;
            }
        }

        controller.AddParameter(name, type);
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (var child in sm.states)
        {
            if (child.state != null && child.state.name == name)
                return child.state;
        }

        return null;
    }

    private static AnimatorState EnsureState(AnimatorStateMachine sm, string name, Vector3 position)
    {
        var state = FindState(sm, name);
        return state != null ? state : sm.AddState(name, position);
    }

    private static BlendTree CreateOrReuseAttackTree(AnimatorController controller, Motion existingAttackMotion)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
        {
            if (asset is BlendTree tree && tree.name == "Orc AttackIndex Tree")
                return tree;
        }

        var attackTree = new BlendTree
        {
            name = "Orc AttackIndex Tree",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "AttackIndex",
            useAutomaticThresholds = false
        };

        AssetDatabase.AddObjectToAsset(attackTree, controller);

        if (existingAttackMotion != null)
            attackTree.AddChild(existingAttackMotion, 0f);

        return attackTree;
    }

    private static void EnsureAnyStateTriggerTransition(
        AnimatorStateMachine sm,
        AnimatorState destination,
        string trigger,
        float duration)
    {
        foreach (var transition in sm.anyStateTransitions)
        {
            if (transition.destinationState == destination && HasCondition(transition, trigger))
                return;
        }

        var t = sm.AddAnyStateTransition(destination);
        t.hasExitTime = false;
        t.duration = duration;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    private static void EnsureAnyStateBoolTransition(
        AnimatorStateMachine sm,
        AnimatorState destination,
        string boolParameter,
        bool value,
        float duration)
    {
        foreach (var transition in sm.anyStateTransitions)
        {
            if (transition.destinationState == destination && HasCondition(transition, boolParameter))
                return;
        }

        var t = sm.AddAnyStateTransition(destination);
        t.hasExitTime = false;
        t.duration = duration;
        t.canTransitionToSelf = false;
        t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, boolParameter);
    }

    private static void EnsureExitTransition(
        AnimatorState source,
        AnimatorState destination,
        float exitTime,
        float duration)
    {
        foreach (var transition in source.transitions)
        {
            if (transition.destinationState == destination && transition.hasExitTime)
                return;
        }

        var t = source.AddTransition(destination);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.duration = duration;
        t.hasFixedDuration = true;
    }

    private static void EnsureBlockExitTransition(AnimatorState block, AnimatorState destination)
    {
        foreach (var transition in block.transitions)
        {
            if (transition.destinationState == destination && HasCondition(transition, "Block"))
                return;
        }

        var t = block.AddTransition(destination);
        t.hasExitTime = false;
        t.duration = 0.1f;
        t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Block");
    }

    private static bool HasCondition(AnimatorStateTransition transition, string parameter)
    {
        foreach (var condition in transition.conditions)
        {
            if (condition.parameter == parameter)
                return true;
        }

        return false;
    }
}
