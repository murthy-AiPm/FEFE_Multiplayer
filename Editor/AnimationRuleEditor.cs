using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Window > FEFE > Animation Rule Editor
/// Displays all rules in an AnimationRuleSet as a sortable table.
/// Edit priority, layer, lock, animation key inline. Changes are saved via Undo/Redo.
/// </summary>
public class AnimationRuleEditor : EditorWindow
{
    // ── State ──────────────────────────────────────────────────────────────
    private AnimationRuleSet _ruleSet;
    private Vector2 _scroll;
    private string _sortColumn = "Priority";
    private bool _sortAscending = true;
    private string _filterText = "";
    private int _filterLayer = -1; // -1 = all

    // Conflict detection
    private HashSet<int> _conflictIndices = new HashSet<int>();

    // Column widths
    private const float COL_IDX      = 32f;
    private const float COL_NAME     = 160f;
    private const float COL_LAYER    = 70f;
    private const float COL_PRIORITY = 70f;
    private const float COL_LOCK     = 44f;
    private const float COL_ANIM_KEY = 180f;
    private const float COL_CONDITIONS = 260f;
    private const float COL_ACTIONS  = 60f;

    // Enum name maps (mirrors AnimationRuleSet.cs enums)
    private static readonly string[] LayerNames     = { "Base", "Action", "Attack" };
    private static readonly string[] BoolParamNames =
    {
        "Moving", "CombatMode", "Modified", "SecondaryHeld", "HoverMode",
        "Grounded", "FreeFall", "IsMounted", "IsTransitioning", "Crouching",
        "Dodging", "IsDodgeStep", "Blocking", "BowDrawing", "BowAiming",
        "Equipping", "Holstering", "WeaponSlot0", "WeaponSlot1", "WeaponSlot2",
        "PendingSlot1", "PendingSlot2"
    };
    private static readonly string[] TriggerModeNames = { "None", "Down", "Held", "Up" };
    private static readonly string[] Direction4Names   = { "Any", "W", "A", "S", "D" };
    private static readonly string[] InputEdgeNames    = { "PrimaryDown", "JumpDown", "ActionHeld" };

    // Styles (built lazily)
    private GUIStyle _headerStyle;
    private GUIStyle _rowEvenStyle;
    private GUIStyle _rowOddStyle;
    private GUIStyle _rowConflictStyle;
    private GUIStyle _centeredLabel;
    private bool _stylesBuilt;

    // ── Menu entry ─────────────────────────────────────────────────────────
    [MenuItem("Window/FEFE/Animation Rule Editor")]
    public static void Open()
    {
        var win = GetWindow<AnimationRuleEditor>("Anim Rule Editor");
        win.minSize = new Vector2(900, 400);
        win.Show();
    }

    // Also open when double-clicking an AnimationRuleSet asset
    [UnityEditor.Callbacks.OnOpenAsset]
    public static bool OnOpenAsset(int instanceID, int line)
    {
        var obj = EditorUtility.InstanceIDToObject(instanceID) as AnimationRuleSet;
        if (obj == null) return false;
        var win = GetWindow<AnimationRuleEditor>("Anim Rule Editor");
        win.minSize = new Vector2(900, 400);
        win._ruleSet = obj;
        win.Show();
        return true;
    }

    // ── GUI ────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        BuildStylesIfNeeded();
        DrawToolbar();

        if (_ruleSet == null)
        {
            EditorGUILayout.HelpBox("Drag an AnimationRuleSet asset here, or open one from the Project window.", MessageType.Info);
            return;
        }

        DetectConflicts();
        DrawConflictWarning();
        DrawTableHeader();
        DrawTableBody();
        DrawFooter();
    }

    // ── Toolbar ────────────────────────────────────────────────────────────
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        // Asset picker
        var newSet = (AnimationRuleSet)EditorGUILayout.ObjectField(
            _ruleSet, typeof(AnimationRuleSet), false, GUILayout.Width(220));
        if (newSet != _ruleSet) { _ruleSet = newSet; _conflictIndices.Clear(); }

        GUILayout.Space(8);

        // Layer filter
        EditorGUILayout.LabelField("Layer:", GUILayout.Width(38));
        string[] layerOptions = { "All", "Base", "Action", "Attack" };
        int filterLayerUI = _filterLayer + 1; // -1→0, 0→1, 1→2, 2→3
        int newFilter = EditorGUILayout.Popup(filterLayerUI, layerOptions, GUILayout.Width(70));
        if (newFilter != filterLayerUI) _filterLayer = newFilter - 1;

        GUILayout.Space(8);

        // Search
        EditorGUILayout.LabelField("Search:", GUILayout.Width(48));
        _filterText = EditorGUILayout.TextField(_filterText, EditorStyles.toolbarSearchField, GUILayout.Width(160));

        GUILayout.FlexibleSpace();

        // Sort by priority button
        if (GUILayout.Button("Sort by Priority ↑", EditorStyles.toolbarButton, GUILayout.Width(110)))
        {
            SortRules("Priority", true);
        }

        // Add rule
        if (_ruleSet != null && GUILayout.Button("+ Add Rule", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            AddNewRule();
        }

        EditorGUILayout.EndHorizontal();
    }

    // ── Conflict detection ─────────────────────────────────────────────────
    private void DetectConflicts()
    {
        _conflictIndices.Clear();
        if (_ruleSet == null) return;

        var rules = _ruleSet.rules;
        for (int i = 0; i < rules.Count; i++)
        {
            for (int j = i + 1; j < rules.Count; j++)
            {
                if (rules[i].layer == rules[j].layer && rules[i].priority == rules[j].priority)
                {
                    _conflictIndices.Add(i);
                    _conflictIndices.Add(j);
                }
            }
        }
    }

    private void DrawConflictWarning()
    {
        if (_conflictIndices.Count == 0) return;

        // Group by layer+priority
        var groups = new Dictionary<(AnimLayer, int), List<string>>();
        foreach (int idx in _conflictIndices)
        {
            var r = _ruleSet.rules[idx];
            var key = (r.layer, r.priority);
            if (!groups.ContainsKey(key)) groups[key] = new List<string>();
            groups[key].Add(r.name);
        }

        string msg = "Priority conflicts: ";
        msg += string.Join(" | ", groups.Select(kv =>
            $"[{LayerNames[(int)kv.Key.Item1]}] priority {kv.Key.Item2} → {string.Join(", ", kv.Value)}"));

        EditorGUILayout.HelpBox(msg, MessageType.Warning);
    }

    // ── Table header ───────────────────────────────────────────────────────
    private void DrawTableHeader()
    {
        EditorGUILayout.BeginHorizontal(_headerStyle);

        DrawSortHeader("#",            COL_IDX,        null);
        DrawSortHeader("Name",         COL_NAME,       "Name");
        DrawSortHeader("Layer",        COL_LAYER,      "Layer");
        DrawSortHeader("Priority",     COL_PRIORITY,   "Priority");
        DrawSortHeader("Lock",         COL_LOCK,       null);
        DrawSortHeader("Anim Key",     COL_ANIM_KEY,   "AnimKey");
        DrawSortHeader("Conditions",   COL_CONDITIONS, null);
        GUILayout.Label("Del", _centeredLabel, GUILayout.Width(COL_ACTIONS));

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSortHeader(string label, float width, string sortKey)
    {
        string display = label;
        if (sortKey != null && _sortColumn == sortKey)
            display += _sortAscending ? " ▲" : " ▼";

        if (GUILayout.Button(display, _headerStyle, GUILayout.Width(width)))
        {
            if (sortKey != null)
                SortRules(sortKey, _sortColumn == sortKey ? !_sortAscending : true);
        }
    }

    // ── Table body ─────────────────────────────────────────────────────────
    private void DrawTableBody()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        var rules = _ruleSet.rules;
        int displayIndex = 0;

        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!PassesFilter(rule)) continue;

            bool isConflict = _conflictIndices.Contains(i);
            GUIStyle rowStyle = isConflict ? _rowConflictStyle
                              : (displayIndex % 2 == 0 ? _rowEvenStyle : _rowOddStyle);

            EditorGUILayout.BeginHorizontal(rowStyle);
            DrawRow(i, rule, isConflict);
            EditorGUILayout.EndHorizontal();

            displayIndex++;
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawRow(int index, AnimationRule rule, bool isConflict)
    {
        // Index
        GUILayout.Label(index.ToString(), _centeredLabel, GUILayout.Width(COL_IDX));

        // Name (editable)
        EditorGUI.BeginChangeCheck();
        string newName = EditorGUILayout.TextField(rule.name, GUILayout.Width(COL_NAME));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_ruleSet, "Rename Rule");
            rule.name = newName;
            EditorUtility.SetDirty(_ruleSet);
        }

        // Layer (popup)
        EditorGUI.BeginChangeCheck();
        int newLayer = EditorGUILayout.Popup((int)rule.layer, LayerNames, GUILayout.Width(COL_LAYER));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_ruleSet, "Change Rule Layer");
            rule.layer = (AnimLayer)newLayer;
            EditorUtility.SetDirty(_ruleSet);
        }

        // Priority (int field) — highlight red if conflict
        Color oldColor = GUI.color;
        if (isConflict) GUI.color = new Color(1f, 0.5f, 0.5f);
        EditorGUI.BeginChangeCheck();
        int newPriority = EditorGUILayout.IntField(rule.priority, GUILayout.Width(COL_PRIORITY));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_ruleSet, "Change Rule Priority");
            rule.priority = newPriority;
            EditorUtility.SetDirty(_ruleSet);
        }
        GUI.color = oldColor;

        // Lock toggle
        EditorGUI.BeginChangeCheck();
        bool newLock = EditorGUILayout.Toggle(rule.lockUntilEnd, GUILayout.Width(COL_LOCK));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_ruleSet, "Toggle Lock");
            rule.lockUntilEnd = newLock;
            EditorUtility.SetDirty(_ruleSet);
        }

        // Anim Key
        EditorGUI.BeginChangeCheck();
        string newKey = EditorGUILayout.TextField(rule.animationKey, GUILayout.Width(COL_ANIM_KEY));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_ruleSet, "Change Anim Key");
            rule.animationKey = newKey;
            EditorUtility.SetDirty(_ruleSet);
        }

        // Conditions summary (read-only, click to expand not yet implemented)
        string condSummary = BuildConditionSummary(rule);
        GUILayout.Label(condSummary, EditorStyles.miniLabel, GUILayout.Width(COL_CONDITIONS));

        // Delete button
        GUI.color = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("✕", GUILayout.Width(COL_ACTIONS)))
        {
            if (EditorUtility.DisplayDialog("Delete Rule",
                $"Delete rule \"{rule.name}\"?", "Delete", "Cancel"))
            {
                Undo.RecordObject(_ruleSet, "Delete Rule");
                _ruleSet.rules.RemoveAt(index);
                EditorUtility.SetDirty(_ruleSet);
            }
        }
        GUI.color = oldColor;
    }

    // ── Footer ─────────────────────────────────────────────────────────────
    private void DrawFooter()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        int total = _ruleSet?.rules.Count ?? 0;
        int visible = _ruleSet?.rules.Count(r => PassesFilter(r)) ?? 0;
        string countLabel = (visible == total)
            ? $"{total} rules"
            : $"{visible} / {total} rules (filtered)";

        GUILayout.Label(countLabel, EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();

        if (_conflictIndices.Count > 0)
        {
            GUI.color = new Color(1f, 0.6f, 0.2f);
            GUILayout.Label($"⚠ {_conflictIndices.Count / 2} conflict(s)", EditorStyles.miniLabel);
            GUI.color = Color.white;
        }

        if (GUILayout.Button("Save Asset", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.EndHorizontal();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool PassesFilter(AnimationRule rule)
    {
        if (_filterLayer >= 0 && (int)rule.layer != _filterLayer) return false;
        if (!string.IsNullOrEmpty(_filterText))
        {
            string f = _filterText.ToLowerInvariant();
            if (!rule.name.ToLowerInvariant().Contains(f) &&
                !rule.animationKey.ToLowerInvariant().Contains(f))
                return false;
        }
        return true;
    }

    private void SortRules(string column, bool ascending)
    {
        if (_ruleSet == null) return;
        _sortColumn = column;
        _sortAscending = ascending;

        Undo.RecordObject(_ruleSet, "Sort Rules");

        _ruleSet.rules.Sort((a, b) =>
        {
            int cmp = column switch
            {
                "Priority" => a.priority.CompareTo(b.priority),
                "Layer"    => ((int)a.layer).CompareTo((int)b.layer),
                "Name"     => string.Compare(a.name, b.name, System.StringComparison.Ordinal),
                "AnimKey"  => string.Compare(a.animationKey, b.animationKey, System.StringComparison.Ordinal),
                _          => 0
            };
            return ascending ? cmp : -cmp;
        });

        EditorUtility.SetDirty(_ruleSet);
    }

    private void AddNewRule()
    {
        Undo.RecordObject(_ruleSet, "Add Rule");
        _ruleSet.rules.Add(new AnimationRule
        {
            name = "New Rule",
            layer = AnimLayer.Base,
            priority = 0,
            animationKey = "",
            all = new System.Collections.Generic.List<RuleCondition>()
        });
        EditorUtility.SetDirty(_ruleSet);
    }

    private string BuildConditionSummary(AnimationRule rule)
    {
        if (rule.all == null || rule.all.Count == 0) return "(no conditions)";

        var parts = new List<string>();
        foreach (var c in rule.all)
        {
            var tokens = new List<string>();

            if (c.useBool && c.boolParam >= 0 && (int)c.boolParam < BoolParamNames.Length)
                tokens.Add($"{BoolParamNames[(int)c.boolParam]}={(c.boolValue ? "T" : "F")}");

            if (c.useInput && (int)c.input < InputEdgeNames.Length && (int)c.triggerMode < TriggerModeNames.Length)
                tokens.Add($"{InputEdgeNames[(int)c.input]}.{TriggerModeNames[(int)c.triggerMode]}");

            if (c.useDirection && c.direction != Direction4.Any && (int)c.direction < Direction4Names.Length)
                tokens.Add($"Dir={Direction4Names[(int)c.direction]}");

            if (c.useActionId)
                tokens.Add($"ActionId={c.actionIdEquals}");

            if (tokens.Count > 0)
                parts.Add(string.Join(" ", tokens));
        }

        return string.Join(" | ", parts);
    }

    // ── Style builder ──────────────────────────────────────────────────────
    private void BuildStylesIfNeeded()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        _headerStyle = new GUIStyle(EditorStyles.toolbar)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
        };

        Color even    = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.88f, 0.88f, 0.88f);
        Color odd     = EditorGUIUtility.isProSkin ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.82f, 0.82f, 0.82f);
        Color conflict = new Color(0.5f, 0.2f, 0.2f);

        _rowEvenStyle     = RowStyle(even);
        _rowOddStyle      = RowStyle(odd);
        _rowConflictStyle = RowStyle(conflict);

        _centeredLabel = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter
        };
    }

    private static GUIStyle RowStyle(Color bg)
    {
        var s = new GUIStyle(GUIStyle.none);
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, bg);
        tex.Apply();
        s.normal.background = tex;
        s.padding = new RectOffset(2, 2, 2, 2);
        return s;
    }
}
