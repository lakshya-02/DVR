using System;
using System.IO;
using Meta.XR;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using DVR;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public static class DvrBuild
{
    private const string Root = "Assets/DVR";
    private const string ScenePath = Root + "/DVR.unity";
    private static readonly Color Ink = Hex(0x111C24);
    private static readonly Color Case = Hex(0x263943);
    private static readonly Color Paper = Hex(0xFFF0D4);
    private static readonly Color Amber = Hex(0xF2AE67);
    private static readonly Color Mint = Hex(0x9BDFC7);

    [MenuItem("DVR/Rebuild prototype scene")]
    public static void Assemble()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        OVRQuickActionsAPI.AddOVRInteractionRig();
        var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
        if (rig == null) throw new InvalidOperationException("Meta interaction rig has no OVRCameraRig.");
        var manager = rig.GetComponent<OVRManager>();
        var managerFields = new SerializedObject(manager);
        managerFields.FindProperty("_trackingOriginType").intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
        managerFields.FindProperty(nameof(OVRManager.isInsightPassthroughEnabled)).boolValue = true;
        managerFields.ApplyModifiedPropertiesWithoutUndo();

        new GameObject("Room passthrough").AddComponent<OVRPassthroughLayer>();
        foreach (var camera in rig.GetComponentsInChildren<Camera>(true))
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
        }

        RenderSettings.skybox = null;
        var station = Place("DVR station", null, Vector3.zero);
        var matte = MaterialAsset("Cabinet", Case);
        var dark = MaterialAsset("Trim", Ink);
        var gold = MaterialAsset("Amber", Amber);
        var mint = MaterialAsset("Mint", Mint);
        var discMaterial = MaterialAsset("Disc", Paper);

        var screen = Place("Television", station, new Vector3(-0.20f, -0.02f, 1.35f));
        Box(screen, "Cabinet", Vector3.zero, new Vector3(0.96f, 0.59f, 0.12f), matte);
        Box(screen, "Bezel", new Vector3(0, 0.022f, -0.063f), new Vector3(0.92f, 0.52f, 0.012f), dark);
        Box(screen, "Power indicator", new Vector3(0.420f, -0.266f, -0.068f), new Vector3(0.013f, 0.01f, 0.006f), mint);
        var pictureCanvas = MakeCanvas("Display", screen, rig.centerEyeAnchor.GetComponent<Camera>(),
            new Vector3(0, 0.022f, -0.071f), new Vector2(896, 504), 0.001f);
        ImageAt(pictureCanvas.transform, "Blank screen", 0, 0, 896, 504, Hex(0x03080A));
        var picture = Ui<RawImage>(pictureCanvas.transform, "Live picture", 0, 0, 896, 504);
        picture.raycastTarget = false;
        var tv = screen.gameObject.AddComponent<Television>();
        var tvProperties = new SerializedObject(tv);
        Set(tvProperties, "picture", picture);
        Set(tvProperties, "demoClip", AssetDatabase.LoadAssetAtPath<VideoClip>(Root + "/Media/OfflineDemo.mp4"));
        tvProperties.ApplyModifiedPropertiesWithoutUndo();

        var deck = Place("Disc player", station, new Vector3(0.02f, -0.35f, 0.56f));
        Box(deck, "Cabinet", new Vector3(0, -0.03f, 0.045f), new Vector3(0.34f, 0.097f, 0.18f), matte);
        Box(deck, "Face", new Vector3(0, -0.03f, -0.048f), new Vector3(0.32f, 0.08f, 0.01f), dark);
        Box(deck, "Open tray", new Vector3(0.026f, -0.013f, -0.098f), new Vector3(0.164f, 0.012f, 0.102f), dark);
        Box(deck, "Tray marker", new Vector3(0.026f, -0.003f, -0.151f), new Vector3(0.082f, 0.005f, 0.006f), gold);
        var slot = Place("Tray socket", deck, new Vector3(0.026f, 0.001f, -0.081f));
        var stand = Place("Disc stand", station, new Vector3(-0.26f, -0.33f, 0.43f));
        Box(stand, "Stand foot", new Vector3(0, -0.086f, 0), new Vector3(0.16f, 0.022f, 0.12f), matte);
        Box(stand, "Stand clip", new Vector3(0, -0.062f, 0), new Vector3(0.082f, 0.04f, 0.05f), dark);
        var discHome = Place("Disc rest", stand, Vector3.zero);
        discHome.localRotation = Quaternion.Euler(-64f, 0, 0);
        var disc = Place("IPTV disc", station, Vector3.zero);
        var discShell = Place("Disc surface", disc, Vector3.zero);
        discShell.gameObject.AddComponent<MeshRenderer>().sharedMaterial = discMaterial;
        discShell.gameObject.AddComponent<MeshFilter>().sharedMesh = DiscMesh();
        var ring = Place("Disc accent", disc, new Vector3(0, 0.0033f, 0));
        ring.gameObject.AddComponent<MeshRenderer>().sharedMaterial = gold;
        ring.gameObject.AddComponent<MeshFilter>().sharedMesh = RingMesh("Disc Accent", 0.018f, 0.028f, 0.0004f);
        var label = MakeCanvas("Disc print", disc, null, new Vector3(0, 0.004f, 0.036f),
            new Vector2(104, 30), 0.00045f);
        label.transform.localRotation = Quaternion.Euler(90f, 0, 0);
        Label(label.transform, "IPTV", 0, 0, 104, 30, 25, Ink, TextAnchor.MiddleCenter);
        var gripCollider = disc.gameObject.AddComponent<BoxCollider>();
        gripCollider.size = new Vector3(0.14f, 0.022f, 0.14f);
        disc.SetPositionAndRotation(discHome.position, discHome.rotation);
        var grabInteractions = QuickActionsAPI.AddGrabInteraction(disc.gameObject).ToArray();
        var rigidbody = disc.GetComponent<Rigidbody>();
        var grabbable = disc.GetComponent<Grabbable>();
        if (rigidbody == null || grabbable == null || grabInteractions.Length == 0)
            throw new InvalidOperationException("Meta grab template did not configure the disc.");

        var canvasCamera = rig.centerEyeAnchor.GetComponent<Camera>();
        var controls = MakeCanvas("DVR controls", station, canvasCamera,
            new Vector3(-0.20f, -0.36f, 1.25f), new Vector2(820, 106), 0.001f);
        ImageAt(controls.transform, "Control backplate", 0, 0, 820, 106, Ink);
        Label(controls.transform, "DVR", -297, 11, 170, 55, 44, Paper, TextAnchor.MiddleLeft);
        var deckStatus = Label(controls.transform, "INSERT DISC", -89, 9, 234, 48, 26, Mint, TextAnchor.MiddleLeft);
        Label(controls.transform, "Grip the disc and place it on the tray.", -175, -30, 415, 28, 20, Paper, TextAnchor.MiddleLeft);
        var recenter = MakeButton(controls.transform, "Recenter", 283, 0, 204, 70);
        QuickActionsAPI.AddRayCanvasInteraction(controls.gameObject);

        var channels = MakeCanvas("Channel panel", station, canvasCamera,
            new Vector3(0.54f, -0.01f, 1.29f), new Vector2(418, 558), 0.001f);
        ImageAt(channels.transform, "Channel backplate", 0, 0, 418, 558, Ink);
        ImageAt(channels.transform, "Header stripe", 0, 276, 418, 6, Amber);
        Label(channels.transform, "IPTV DISC", 0, 213, 354, 50, 39, Paper, TextAnchor.MiddleLeft);
        var status = Label(channels.transform, "Choose a live channel.", 0, 145, 354, 88, 25, Mint, TextAnchor.UpperLeft);
        Button[] rows =
        {
            MakeButton(channels.transform, "Channel 1", 0, 59, 354, 62),
            MakeButton(channels.transform, "Channel 2", 0, -13, 354, 62),
            MakeButton(channels.transform, "Channel 3", 0, -85, 354, 62)
        };
        var demo = MakeButton(channels.transform, "Play offline demo", 0, -157, 354, 56);
        var random = MakeButton(channels.transform, "Random channel", -69, -228, 216, 62);
        var eject = MakeButton(channels.transform, "Eject", 118, -228, 118, 62);
        QuickActionsAPI.AddRayCanvasInteraction(channels.gameObject);

        var deckController = new GameObject("DVR session").AddComponent<DiscDeck>();
        var fields = new SerializedObject(deckController);
        Set(fields, "head", rig.centerEyeAnchor);
        Set(fields, "station", station);
        Set(fields, "stand", discHome);
        Set(fields, "slot", slot);
        Set(fields, "disc", rigidbody);
        Set(fields, "grabbable", grabbable);
        SetArray(fields, "grabInteractions", grabInteractions);
        Set(fields, "channelPanel", channels.gameObject);
        SetArray(fields, "rows", rows);
        Set(fields, "randomButton", random);
        Set(fields, "demoButton", demo);
        Set(fields, "deckStatus", deckStatus);
        Set(fields, "status", status);
        Set(fields, "television", tv);
        fields.ApplyModifiedPropertiesWithoutUndo();
        UnityEventTools.AddPersistentListener(recenter.onClick, deckController.Recenter);
        UnityEventTools.AddPersistentListener(eject.onClick, deckController.Eject);
        UnityEventTools.AddPersistentListener(random.onClick, deckController.RandomChannel);
        UnityEventTools.AddPersistentListener(demo.onClick, deckController.PlayDemo);
        channels.gameObject.SetActive(false);

        ConfigureAndroid();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("DVR scene could not be saved.");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[DVR] Prototype scene assembled");
    }

    public static void Android()
    {
        if (!File.Exists(ScenePath)) Assemble();
        else ConfigureAndroid();
        var args = Environment.GetCommandLineArgs();
        var outputAt = Array.IndexOf(args, "-buildOutput") + 1;
        var output = outputAt > 0 && outputAt < args.Length ? args[outputAt] : "Builds/DVR.apk";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.Development
        });
        if (build.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Android build {build.summary.result} ({build.summary.totalErrors} errors)");
        Debug.Log($"[DVR] APK built at {output}");
    }

    private static void ConfigureAndroid()
    {
        ConfigureOpenXR();
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.companyName = "Lakshya";
        PlayerSettings.productName = "DVR";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.lakshya.dvr");
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.forceInternetPermission = true;
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });

        var config = OVRProjectConfig.CachedProjectConfig;
        config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
        EditorUtility.SetDirty(config);
        OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);

        // The SDK generates a disabled developer agent with machine-specific settings.
        var localAgent = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/DevAgentSettings.asset");
        if (localAgent != null)
        {
            var settings = new SerializedObject(localAgent);
            settings.FindProperty("enabled").boolValue = false;
            settings.FindProperty("serverAddress").stringValue = "";
            settings.FindProperty("accessToken").stringValue = "";
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
    }

    private static void ConfigureOpenXR()
    {
        const BuildTargetGroup target = BuildTargetGroup.Android;
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings))
        {
            settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            Directory.CreateDirectory("Assets/XR");
            AssetDatabase.CreateAsset(settings, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
        }

        if (!settings.HasManagerSettingsForBuildTarget(target))
            settings.CreateDefaultManagerSettingsForBuildTarget(target);

        var general = settings.SettingsForBuildTarget(target);
        general.InitManagerOnStart = true;
        const string loader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        if (!XRPackageMetadataStore.IsLoaderAssigned(loader, target)
            && !XRPackageMetadataStore.AssignLoader(general.Manager, loader, target))
            throw new InvalidOperationException("Could not assign the Android OpenXR loader.");

        FeatureHelpers.RefreshFeatures(target);
        var openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(target);
        openXR.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        Enable<MetaQuestFeature>(openXR);
        Enable<MetaXRFeature>(openXR);
        Enable<OculusTouchControllerProfile>(openXR);
        Enable<MetaQuestTouchPlusControllerProfile>(openXR);
        EditorUtility.SetDirty(openXR);
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(general.Manager);
        AssetDatabase.SaveAssets();
    }

    private static void Enable<T>(OpenXRSettings settings) where T : OpenXRFeature
    {
        var feature = settings.GetFeature<T>();
        if (feature == null) throw new InvalidOperationException($"Missing OpenXR feature {typeof(T).Name}.");

        feature.enabled = true;
        EditorUtility.SetDirty(feature);
    }

    private static Color Hex(uint hex) =>
        new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);

    private static Transform Place(string name, Transform parent, Vector3 offset)
    {
        var position = new GameObject(name).transform;
        position.SetParent(parent, false);
        position.localPosition = offset;
        return position;
    }

    private static void Box(Transform parent, string name, Vector3 center, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = center;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
    }

    private static Material MaterialAsset(string name, Color color)
    {
        var path = $"{Root}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Unlit/Color")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh DiscMesh() => RingMesh("Disc Mesh", 0.012f, 0.070f, 0.006f);

    private static Mesh RingMesh(string name, float inner, float outer, float thickness)
    {
        var path = $"{Root}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int segments = 64;
        var vertices = new Vector3[4 * segments];
        var triangles = new int[24 * segments];
        for (var index = 0; index < segments; index++)
        {
            var theta = Mathf.PI * 2 * index / segments;
            var radial = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
            var top = Vector3.up * (thickness / 2);
            var vertex = 4 * index;
            vertices[vertex] = radial * inner + top;
            vertices[vertex + 1] = radial * outer + top;
            vertices[vertex + 2] = radial * inner - top;
            vertices[vertex + 3] = radial * outer - top;
        }

        for (var index = 0; index < segments; index++)
        {
            var v = 4 * index;
            var n = 4 * ((index + 1) % segments);
            var offset = 24 * index;
            var faces = new[] { v, n, n+1, v, n+1, v+1, v+2, v+3, n+3, v+2, n+3, n+2,
                v+1, n+1, n+3, v+1, n+3, v+3, v, v+2, n+2, v, n+2, n };
            Array.Copy(faces, 0, triangles, offset, faces.Length);
        }

        var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static Canvas MakeCanvas(string name, Transform parent, Camera camera, Vector3 center, Vector2 size, float scale)
    {
        var canvas = new GameObject(name, typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        var rect = (RectTransform)canvas.transform;
        rect.SetParent(parent, false);
        rect.localPosition = center;
        rect.localScale = Vector3.one * scale;
        rect.sizeDelta = size;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        return canvas;
    }

    private static T Ui<T>(Transform parent, string name, float x, float y, float width, float height) where T : Component
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(T));
        var rect = (RectTransform)item.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return item.GetComponent<T>();
    }

    private static Image ImageAt(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var image = Ui<Image>(parent, name, x, y, width, height);
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text Label(Transform parent, string text, float x, float y, float width, float height, int size, Color color, TextAnchor align)
    {
        var label = Ui<Text>(parent, text, x, y, width, height);
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text;
        label.fontSize = size;
        label.fontStyle = FontStyle.Bold;
        label.color = color;
        label.alignment = align;
        label.raycastTarget = false;
        return label;
    }

    private static Button MakeButton(Transform parent, string name, float x, float y, float width, float height)
    {
        var image = ImageAt(parent, name, x, y, width, height, Color.white);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = Case;
        colors.highlightedColor = Hex(0x3B5A63);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Hex(0x5B4434);
        colors.disabledColor = Hex(0x283036);
        button.colors = colors;
        button.targetGraphic = image;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        Label(button.transform, name, 0, 0, width - 12, height - 6, 23, Paper, TextAnchor.MiddleCenter);
        return button;
    }

    private static void Set(SerializedObject target, string name, UnityEngine.Object value) =>
        target.FindProperty(name).objectReferenceValue = value;

    private static void SetArray(SerializedObject target, string name, UnityEngine.Object[] values)
    {
        var array = target.FindProperty(name);
        array.arraySize = values.Length;
        for (var index = 0; index < values.Length; index++)
            array.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
    }
}
