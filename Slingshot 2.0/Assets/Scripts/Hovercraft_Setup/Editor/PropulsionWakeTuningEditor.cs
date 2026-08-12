using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PropulsionWakeTuning))]
public sealed class PropulsionWakeTuningEditor : Editor
{
    private enum HandleElement
    {
        ExhaustPipe,
        ExhaustEffect
    }

    private enum HandleSide { Left, Right }

    private SerializedProperty _overallLength;
    private SerializedProperty _bodyLength;
    private SerializedProperty _filamentLength;
    private SerializedProperty _memoryLength;
    private SerializedProperty _showMemory;
    private SerializedProperty _maximumMemoryLength;
    private SerializedProperty _memorySmoothness;
    private SerializedProperty _preventForwardWrap;
    private SerializedProperty _forwardWrapAllowance;
    private SerializedProperty _clearOnDirectionChange;
    private SerializedProperty _directionClearAngle;
    private SerializedProperty _teleportDistance;
    private SerializedProperty _leftOffset;
    private SerializedProperty _rightOffset;
    private SerializedProperty _pivot;
    private SerializedProperty _width;
    private SerializedProperty _coreSize;
    private SerializedProperty _plumeSize;
    private SerializedProperty _filamentSize;
    private SerializedProperty _ribbonSize;
    private SerializedProperty _particleStretch;
    private SerializedProperty _showSparks;
    private SerializedProperty _leftExhaustEffectOffset;
    private SerializedProperty _rightExhaustEffectOffset;
    private SerializedProperty _chamberColor;
    private SerializedProperty _chamberSize;
    private SerializedProperty _chamberIntensity;
    private SerializedProperty _sparkColor;
    private SerializedProperty _sparkAmount;
    private SerializedProperty _sparkSize;
    private SerializedProperty _sparkSpeed;
    private SerializedProperty _sparkSpread;
    private SerializedProperty _sparkLifetime;
    private SerializedProperty _sparkStreakLength;
    private SerializedProperty _sparkTrailAmount;
    private SerializedProperty _sparkTurbulence;
    private SerializedProperty _overchargeSparkBoost;
    private SerializedProperty _density;
    private bool _showLayerPlacement;
    private bool _showMoteControls;
    private bool _showChamberFinePlacement;
    private bool _showSparkPlacement = true;
    private bool _showSparkMotion;
    private bool _showSparkTrails;
    private bool _showSparkResponse;
    private bool _showAdvanced;
    private HandleElement _handleElement = HandleElement.ExhaustPipe;
    private HandleSide _handleSide = HandleSide.Left;

    private void OnEnable()
    {
        _overallLength = serializedObject.FindProperty("overallLength");
        _bodyLength = serializedObject.FindProperty("exhaustBodyLength");
        _filamentLength = serializedObject.FindProperty("filamentLength");
        _memoryLength = serializedObject.FindProperty("memoryLength");
        _showMemory = serializedObject.FindProperty("showMemoryRibbons");
        _maximumMemoryLength = serializedObject.FindProperty("maximumMemoryLengthMeters");
        _memorySmoothness = serializedObject.FindProperty("memorySmoothness");
        _preventForwardWrap = serializedObject.FindProperty("preventForwardWrap");
        _forwardWrapAllowance = serializedObject.FindProperty("forwardWrapAllowance");
        _clearOnDirectionChange = serializedObject.FindProperty("clearOnSharpDirectionChange");
        _directionClearAngle = serializedObject.FindProperty("directionChangeClearAngle");
        _teleportDistance = serializedObject.FindProperty("teleportClearDistance");
        _leftOffset = serializedObject.FindProperty("leftEffectOffset");
        _rightOffset = serializedObject.FindProperty("rightEffectOffset");
        _pivot = serializedObject.FindProperty("stretchedParticlePivot");
        _width = serializedObject.FindProperty("width");
        _coreSize = serializedObject.FindProperty("coreSize");
        _plumeSize = serializedObject.FindProperty("plumeSize");
        _filamentSize = serializedObject.FindProperty("filamentSize");
        _ribbonSize = serializedObject.FindProperty("ribbonSize");
        _particleStretch = serializedObject.FindProperty("particleStretch");
        _showSparks = serializedObject.FindProperty("showPlasmaSparks");
        _leftExhaustEffectOffset = serializedObject.FindProperty("leftExhaustEffectOffset");
        _rightExhaustEffectOffset = serializedObject.FindProperty("rightExhaustEffectOffset");
        _chamberColor = serializedObject.FindProperty("plasmaChamberColor");
        _chamberSize = serializedObject.FindProperty("chamberSize");
        _chamberIntensity = serializedObject.FindProperty("chamberIntensity");
        _sparkColor = serializedObject.FindProperty("plasmaSparkColor");
        _sparkAmount = serializedObject.FindProperty("sparkAmount");
        _sparkSize = serializedObject.FindProperty("sparkSize");
        _sparkSpeed = serializedObject.FindProperty("sparkSpeed");
        _sparkSpread = serializedObject.FindProperty("sparkSpread");
        _sparkLifetime = serializedObject.FindProperty("sparkLifetime");
        _sparkStreakLength = serializedObject.FindProperty("sparkStreakLength");
        _sparkTrailAmount = serializedObject.FindProperty("sparkTrailAmount");
        _sparkTurbulence = serializedObject.FindProperty("sparkTurbulence");
        _overchargeSparkBoost = serializedObject.FindProperty("overchargeSparkBoost");
        _density = serializedObject.FindProperty("particleDensity");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Tune in Play Mode from top to bottom. Start with a preset, then adjust Length and Width. " +
            "Use alignment only after the silhouette feels right.", MessageType.Info);

        DrawPresets();
        EditorGUILayout.Space(8f);

        EditorGUILayout.LabelField("PRIMARY LOOK", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_overallLength, new GUIContent("Length"));
        EditorGUILayout.PropertyField(_width, new GUIContent("Thickness"));
        EditorGUILayout.PropertyField(_density, new GUIContent("Detail Density"));
        EditorGUILayout.PropertyField(_showMemory, new GUIContent("Movement Ribbons"));
        if (_showMemory.boolValue)
            EditorGUILayout.PropertyField(_maximumMemoryLength,
                new GUIContent("Ribbon Max Length (m)"));

        DrawEffectiveLengthReadout();
        EditorGUILayout.Space(8f);

        EditorGUILayout.LabelField("TRAIL SILHOUETTE", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Width controls do not change trail duration. Streak Length changes the elongated particle shape without adding movement history.",
            MessageType.None);
        EditorGUILayout.PropertyField(_coreSize, new GUIContent("Hot Core Width"));
        DrawProperty("showHotCore", "Show Hot Core");
        EditorGUILayout.PropertyField(_plumeSize, new GUIContent("Plasma Plume Width"));
        DrawProperty("showPlasmaPlume", "Show Plasma Plume");
        EditorGUILayout.PropertyField(_filamentSize, new GUIContent("Ion Filament Width"));
        DrawProperty("showIonFilaments", "Show Ion Filaments");
        EditorGUILayout.PropertyField(_ribbonSize, new GUIContent("Memory Ribbon Width"));
        EditorGUILayout.PropertyField(_particleStretch, new GUIContent("Particle Streak Length"));
        EditorGUILayout.Space(8f);

        EditorGUILayout.LabelField("EXHAUST PIPE PLACEMENT", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "The LEFT/RIGHT exhaust transforms are the placement authority. Move them with the Exhaust Pipe Scene gizmo: trail, core, filaments, chamber, motes, and sparks all follow.",
            MessageType.None);
        EditorGUILayout.Space(8f);

        _showLayerPlacement = EditorGUILayout.Foldout(_showLayerPlacement,
            "FINE INDIVIDUAL LAYER CORRECTIONS", true);
        if (_showLayerPlacement)
        {
            EditorGUILayout.HelpBox(
                "Normally leave these at zero. They adjust one visual layer relative to the whole trail placement above.",
                MessageType.None);
            DrawLayerPlacement("Hot Core", "leftCoreOffset", "rightCoreOffset",
                "leftCoreRotation", "rightCoreRotation", true);
            DrawLayerPlacement("Plasma Plume", "leftPlumeOffset", "rightPlumeOffset",
                "leftPlumeRotation", "rightPlumeRotation", true);
            DrawLayerPlacement("Ion Filaments", "leftFilamentOffset", "rightFilamentOffset",
                "leftFilamentRotation", "rightFilamentRotation", true);
            DrawLayerPlacement("Memory Ribbon", "leftRibbonOffset", "rightRibbonOffset",
                "leftRibbonRotation", "rightRibbonRotation", true);
            if (GUILayout.Button("Reset All Exhaust Layer Placement"))
            {
                ResetVectors("leftCoreOffset", "rightCoreOffset", "leftCoreRotation",
                    "rightCoreRotation", "leftPlumeOffset", "rightPlumeOffset",
                    "leftPlumeRotation", "rightPlumeRotation", "leftFilamentOffset",
                    "rightFilamentOffset", "leftFilamentRotation", "rightFilamentRotation",
                    "leftRibbonOffset", "rightRibbonOffset", "leftRibbonRotation",
                    "rightRibbonRotation");
            }
            EditorGUILayout.Space(8f);
        }

        EditorGUILayout.LabelField("EXHAUST EFFECT", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_showSparks,
            new GUIContent("Enable Expelled Sparks"));
        EditorGUILayout.LabelField("Sparks / Glow Assembly Placement",
            EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox(
            "A fine adjustment relative to the exhaust pipe. The Exhaust Effect Scene gizmo moves chamber glow, motes, and sparks together.",
            MessageType.None);
        EditorGUILayout.PropertyField(_leftExhaustEffectOffset,
            new GUIContent("Left Exhaust Effect Position"));
        EditorGUILayout.PropertyField(_rightExhaustEffectOffset,
            new GUIContent("Right Exhaust Effect Position"));
        DrawProperty("leftExhaustEffectRotation", "Left Exhaust Effect Aim");
        DrawProperty("rightExhaustEffectRotation", "Right Exhaust Effect Aim");
        if (GUILayout.Button("Reset Exhaust Effect Placement"))
        {
            _leftExhaustEffectOffset.vector3Value = Vector3.zero;
            _rightExhaustEffectOffset.vector3Value = Vector3.zero;
            ResetVectors("leftExhaustEffectRotation", "rightExhaustEffectRotation");
        }

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Pipe Chamber", EditorStyles.miniBoldLabel);
            DrawProperty("showPlasmaChamber", "Enable Chamber Glow");
            _showChamberFinePlacement = EditorGUILayout.Foldout(
                _showChamberFinePlacement, "Fine Chamber Position (Relative)", true);
            if (_showChamberFinePlacement)
            {
                EditorGUILayout.HelpBox(
                    "Optional correction relative to Exhaust Effect Position. Leave at zero when moving the complete sparks/glow assembly.",
                    MessageType.None);
                DrawProperty("leftChamberOffset", "Left Fine Position");
                DrawProperty("rightChamberOffset", "Right Fine Position");
            }
            EditorGUILayout.PropertyField(_chamberColor, new GUIContent("Chamber Color"));
            EditorGUILayout.PropertyField(_chamberSize, new GUIContent("Chamber Size"));
            EditorGUILayout.PropertyField(_chamberIntensity,
                new GUIContent("Chamber Intensity"));

            EditorGUILayout.Space(3f);
            _showMoteControls = EditorGUILayout.Foldout(_showMoteControls,
                "Loose Plasma Motes", true);
            if (_showMoteControls)
            {
                DrawProperty("showPlasmaMotes", "Enable Motes");
                DrawLayerPlacement("Motes", "leftMoteOffset", "rightMoteOffset",
                    "leftMoteRotation", "rightMoteRotation", true);
                DrawProperty("moteAmount", "Amount");
                DrawProperty("moteSize", "Size");
                DrawProperty("moteSpeed", "Speed");
                DrawProperty("moteSpread", "Spread");
                DrawProperty("moteLifetime", "Lifetime");
                DrawProperty("moteTurbulence", "Turbulence");
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Expelled Sparks", EditorStyles.miniBoldLabel);
            _showSparkPlacement = EditorGUILayout.Foldout(_showSparkPlacement,
                "Spark Placement and Aim", true);
            if (_showSparkPlacement)
            {
                DrawLayerPlacement("Sparks", "leftSparkOffset", "rightSparkOffset",
                    "leftSparkRotation", "rightSparkRotation", true);
                DrawProperty("sparkSpawnRadius", "Spawn Radius");
            }
            EditorGUILayout.PropertyField(_sparkColor, new GUIContent("Spark Color"));
            EditorGUILayout.PropertyField(_sparkAmount, new GUIContent("Amount"));
            EditorGUILayout.PropertyField(_sparkSize, new GUIContent("Spark Size"));

            _showSparkMotion = EditorGUILayout.Foldout(_showSparkMotion,
                "Spark Motion", true);
            if (_showSparkMotion)
            {
                EditorGUILayout.PropertyField(_sparkSpeed, new GUIContent("Ejection Speed"));
                DrawProperty("sparkSpeedVariation", "Speed Variation");
                EditorGUILayout.PropertyField(_sparkSpread, new GUIContent("Scatter Width"));
                EditorGUILayout.PropertyField(_sparkLifetime, new GUIContent("Visible Lifetime"));
                DrawProperty("sparkDrag", "Drag");
                DrawProperty("sparkGravity", "Gravity");
                EditorGUILayout.PropertyField(_sparkTurbulence,
                    new GUIContent("Turbulence Strength"));
                DrawProperty("sparkTurbulenceFrequency", "Turbulence Detail");
            }

            _showSparkTrails = EditorGUILayout.Foldout(_showSparkTrails,
                "Spark Streaks and Trails", true);
            if (_showSparkTrails)
            {
                EditorGUILayout.PropertyField(_sparkStreakLength,
                    new GUIContent("Spark Streak Length"));
                EditorGUILayout.PropertyField(_sparkTrailAmount,
                    new GUIContent("After-Trail Amount"));
                DrawProperty("sparkTrailLifetime", "After-Trail Lifetime");
                DrawProperty("sparkTrailWidth", "After-Trail Width");
            }

            _showSparkResponse = EditorGUILayout.Foldout(_showSparkResponse,
                "Engine Response", true);
            if (_showSparkResponse)
            {
                DrawProperty("idleSparkPresence", "Idle Presence");
                DrawProperty("throttleSparkResponse", "Throttle Response");
                DrawProperty("sparkFlickerSpeed", "Flicker Speed");
                DrawProperty("ignitionSparkBurst", "Ignition Burst");
                EditorGUILayout.PropertyField(_overchargeSparkBoost,
                    new GUIContent("Overcharge Intensity"));
            }

            if (GUILayout.Button("Reset All Plasma Element Placement"))
            {
                ResetVectors("leftExhaustEffectOffset", "rightExhaustEffectOffset",
                    "leftExhaustEffectRotation", "rightExhaustEffectRotation",
                    "leftChamberOffset", "rightChamberOffset", "leftMoteOffset",
                    "rightMoteOffset", "leftMoteRotation", "rightMoteRotation",
                    "leftSparkOffset", "rightSparkOffset", "leftSparkRotation",
                    "rightSparkRotation");
            }
        EditorGUILayout.Space(8f);

        EditorGUILayout.LabelField("SCENE PLACEMENT GIZMOS", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Exhaust Pipe moves the real nozzle and every effect. Exhaust Effect moves only sparks/glow relative to that pipe.",
            MessageType.None);
        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Scene Placement Handle", EditorStyles.miniBoldLabel);
        _handleElement = (HandleElement)EditorGUILayout.EnumPopup("Element", _handleElement);
        _handleSide = (HandleSide)EditorGUILayout.EnumPopup("Side", _handleSide);
        EditorGUILayout.HelpBox(
            "Select the hovercraft's Propulsion Wake Placement Rig, choose Exhaust Pipe or Exhaust Effect, then place each side directly in the Scene view.",
            MessageType.None);
        EditorGUILayout.Space(8f);
        _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced Layer Controls", true);
        if (_showAdvanced)
            DrawAdvanced();

        serializedObject.ApplyModifiedProperties();
    }

    private void OnSceneGUI()
    {
        PropulsionWakeTuning tuning = (PropulsionWakeTuning)target;
        if (tuning == null || tuning.transform == null || serializedObject == null)
            return;

        if (_handleElement == HandleElement.ExhaustPipe)
        {
            Transform nozzle = ResolveExhaustAnchor(tuning, _handleSide == HandleSide.Left);
            if (nozzle == null)
                return;

            Handles.color = _handleSide == HandleSide.Left
                ? new Color(0.15f, 0.95f, 1f, 1f)
                : new Color(0.78f, 0.35f, 1f, 1f);
            EditorGUI.BeginChangeCheck();
            Vector3 movedPipe = Handles.PositionHandle(nozzle.position, nozzle.rotation);
            Quaternion aimedPipe = Handles.RotationHandle(nozzle.rotation, movedPipe);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(nozzle, "Place exhaust pipe effect anchor");
                nozzle.SetPositionAndRotation(movedPipe, aimedPipe);
                EditorUtility.SetDirty(nozzle);
                PrefabUtility.RecordPrefabInstancePropertyModifications(nozzle);
            }
            Handles.Label(movedPipe, $"  {_handleSide} Exhaust Pipe - ALL TRAILS",
                EditorStyles.boldLabel);
            return;
        }

        if (!TryResolveHandleTarget(tuning, out Transform anchor,
            out string positionPropertyName, out string rotationPropertyName,
            out Vector3 masterOffset, out Vector3 masterEulerRotation))
            return;

        // Prefab refreshes, Undo and Play/Edit transitions can destroy an anchor
        // between selection and the next SceneView repaint.
        if (anchor == null)
            return;

        serializedObject.Update();
        SerializedProperty positionProperty = serializedObject.FindProperty(positionPropertyName);
        SerializedProperty rotationProperty = string.IsNullOrEmpty(rotationPropertyName)
            ? null
            : serializedObject.FindProperty(rotationPropertyName);
        if (positionProperty == null)
            return;

        Quaternion masterRotation = Quaternion.Euler(masterEulerRotation);
        Vector3 totalLocalPosition = masterOffset +
            masterRotation * positionProperty.vector3Value;
        Vector3 worldPosition = anchor.TransformPoint(totalLocalPosition);
        Quaternion localRotation = rotationProperty != null
            ? Quaternion.Euler(rotationProperty.vector3Value)
            : Quaternion.identity;
        Quaternion worldRotation = anchor.rotation * masterRotation * localRotation;

        Handles.color = _handleSide == HandleSide.Left
            ? new Color(1f, 0.28f, 0.60f, 1f)
            : new Color(1f, 0.65f, 0.15f, 1f);
        EditorGUI.BeginChangeCheck();
        Vector3 movedPosition = Handles.PositionHandle(worldPosition, worldRotation);
        Quaternion movedRotation = rotationProperty != null
            ? Handles.RotationHandle(worldRotation, movedPosition)
            : worldRotation;
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(tuning, "Place propulsion VFX element");
            positionProperty.vector3Value = Quaternion.Inverse(masterRotation) *
                (anchor.InverseTransformPoint(movedPosition) - masterOffset);
            if (rotationProperty != null)
            {
                Quaternion movedLocal = Quaternion.Inverse(anchor.rotation * masterRotation) *
                    movedRotation;
                rotationProperty.vector3Value = movedLocal.eulerAngles;
            }
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(tuning);
        }

        Handles.Label(movedPosition,
            $"  {_handleSide} Exhaust Effect - SPARKS + GLOW", EditorStyles.boldLabel);
    }

    private bool TryResolveHandleTarget(PropulsionWakeTuning tuning,
        out Transform anchor, out string positionProperty, out string rotationProperty,
        out Vector3 masterOffset, out Vector3 masterEulerRotation)
    {
        anchor = null;
        positionProperty = null;
        rotationProperty = null;
        masterOffset = Vector3.zero;
        masterEulerRotation = Vector3.zero;
        if (tuning == null || tuning.transform == null)
            return false;

        bool left = _handleSide == HandleSide.Left;
        anchor = ResolveExhaustAnchor(tuning, left);

        string side = left ? "left" : "right";
        positionProperty = side + "ExhaustEffectOffset";
        rotationProperty = side + "ExhaustEffectRotation";
        return anchor != null;
    }

    private static Transform ResolveExhaustAnchor(PropulsionWakeTuning tuning, bool left)
    {
        return tuning == null ? null : FindDescendant(tuning.transform,
            left ? "LEFT EXHAUST - PLACE ON NOZZLE"
                 : "RIGHT EXHAUST - PLACE ON NOZZLE");
    }

    private void DrawProperty(string propertyName, string label)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            EditorGUILayout.PropertyField(property, new GUIContent(label));
    }

    private void DrawLayerPlacement(string label, string leftPosition,
        string rightPosition, string leftRotation, string rightRotation,
        bool showRotation)
    {
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
        DrawProperty(leftPosition, "Left Position");
        DrawProperty(rightPosition, "Right Position");
        if (showRotation)
        {
            DrawProperty(leftRotation, "Left Aim Rotation");
            DrawProperty(rightRotation, "Right Aim Rotation");
        }
    }

    private void ResetVectors(params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Vector3)
                property.vector3Value = Vector3.zero;
        }
    }

    private static Transform FindDescendant(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrEmpty(exactName))
            return null;

        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            // Unity can leave destroyed-object entries in this array for the duration
            // of an Undo/prefab/Play transition. Treat them as absent.
            if (descendant != null && string.Equals(descendant.name, exactName,
                    System.StringComparison.Ordinal))
                return descendant;
        }
        return null;
    }

    private void DrawPresets()
    {
        EditorGUILayout.LabelField("STARTING PRESETS", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clean")) ApplyPreset(0.75f, 0.82f, 0.65f, 0.75f, 8f, true);
            if (GUILayout.Button("Race")) ApplyPreset(1f, 1f, 0.9f, 1f, 12f, true);
            if (GUILayout.Button("Cinematic")) ApplyPreset(1.45f, 1.22f, 1.25f, 1.35f, 18f, true);
        }
        if (GUILayout.Button("Reset Everything"))
            ApplyPreset(1f, 1f, 1f, 1f, 12f, true, true);
    }

    private void ApplyPreset(float length, float width, float density,
        float memory, float memoryMeters, bool showMemory, bool fullReset = false)
    {
        _overallLength.floatValue = length;
        _width.floatValue = width;
        _density.floatValue = density;
        _memoryLength.floatValue = memory;
        _maximumMemoryLength.floatValue = memoryMeters;
        _showMemory.boolValue = showMemory;
        _bodyLength.floatValue = 1f;
        _filamentLength.floatValue = 1f;
        _coreSize.floatValue = 1f;
        _plumeSize.floatValue = 1f;
        _filamentSize.floatValue = 1f;
        _ribbonSize.floatValue = 1f;
        _particleStretch.floatValue = 1f;
        _showSparks.boolValue = true;
        _chamberSize.floatValue = 1f;
        _chamberIntensity.floatValue = 1f;
        _sparkAmount.floatValue = 1f;
        _sparkSize.floatValue = 1f;
        _sparkSpeed.floatValue = 1f;
        _sparkSpread.floatValue = 1f;
        _sparkLifetime.floatValue = 1f;
        _sparkStreakLength.floatValue = 1f;
        _sparkTrailAmount.floatValue = 0.55f;
        _sparkTurbulence.floatValue = 1f;
        _overchargeSparkBoost.floatValue = 1f;
        if (fullReset)
        {
            _leftOffset.vector3Value = Vector3.zero;
            _rightOffset.vector3Value = Vector3.zero;
            _pivot.floatValue = 0.5f;
            _memorySmoothness.floatValue = 0.72f;
            _preventForwardWrap.boolValue = true;
            _forwardWrapAllowance.floatValue = 0.35f;
            _clearOnDirectionChange.boolValue = true;
            _directionClearAngle.floatValue = 52f;
            _teleportDistance.floatValue = 18f;
            ResetVectors("leftCoreOffset", "rightCoreOffset", "leftCoreRotation",
                "rightCoreRotation", "leftPlumeOffset", "rightPlumeOffset",
                "leftPlumeRotation", "rightPlumeRotation", "leftFilamentOffset",
                "rightFilamentOffset", "leftFilamentRotation", "rightFilamentRotation",
                "leftRibbonOffset", "rightRibbonOffset", "leftRibbonRotation",
                "rightRibbonRotation", "leftExhaustEffectOffset", "rightExhaustEffectOffset",
                "leftExhaustEffectRotation", "rightExhaustEffectRotation",
                "leftChamberOffset", "rightChamberOffset", "leftMoteOffset",
                "rightMoteOffset", "leftMoteRotation", "rightMoteRotation",
                "leftSparkOffset", "rightSparkOffset", "leftSparkRotation",
                "rightSparkRotation");
            SetBool("showHotCore", true);
            SetBool("showPlasmaPlume", true);
            SetBool("showIonFilaments", true);
            SetBool("showPlasmaChamber", true);
            SetBool("showPlasmaMotes", true);
            SetFloat("moteAmount", 1f);
            SetFloat("moteSize", 1f);
            SetFloat("moteSpeed", 1f);
            SetFloat("moteSpread", 1f);
            SetFloat("moteLifetime", 1f);
            SetFloat("moteTurbulence", 1f);
            SetFloat("sparkSpawnRadius", 1f);
            SetFloat("sparkSpeedVariation", 0.65f);
            SetFloat("sparkDrag", 0.25f);
            SetFloat("sparkGravity", 0f);
            SetFloat("sparkTrailLifetime", 1f);
            SetFloat("sparkTrailWidth", 1f);
            SetFloat("idleSparkPresence", 1f);
            SetFloat("throttleSparkResponse", 1f);
            SetFloat("ignitionSparkBurst", 1f);
            SetFloat("sparkFlickerSpeed", 1f);
            SetFloat("sparkTurbulenceFrequency", 1f);
        }
    }

    private void SetFloat(string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    private void SetBool(string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    private void DrawEffectiveLengthReadout()
    {
        float body = _overallLength.floatValue * _bodyLength.floatValue;
        float filaments = _overallLength.floatValue * _filamentLength.floatValue;
        float memory = _overallLength.floatValue * _memoryLength.floatValue;
        EditorGUILayout.HelpBox(
            $"Effective: Body {body:0.00}x | Filaments {filaments:0.00}x | Ribbons {memory:0.00}x",
            body > 4f || filaments > 4f || memory > 4f ? MessageType.Warning : MessageType.None);
    }

    private void DrawAdvanced()
    {
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_bodyLength, new GUIContent("Body Layer Multiplier"));
        EditorGUILayout.PropertyField(_filamentLength, new GUIContent("Filament Layer Multiplier"));
        EditorGUILayout.PropertyField(_memoryLength, new GUIContent("Ribbon Lifetime Multiplier"));
        EditorGUILayout.PropertyField(_memorySmoothness);
        EditorGUILayout.PropertyField(_preventForwardWrap);
        if (_preventForwardWrap.boolValue)
            EditorGUILayout.PropertyField(_forwardWrapAllowance);
        EditorGUILayout.PropertyField(_clearOnDirectionChange);
        if (_clearOnDirectionChange.boolValue)
            EditorGUILayout.PropertyField(_directionClearAngle);
        EditorGUILayout.PropertyField(_teleportDistance);
        EditorGUI.indentLevel--;
    }
}
