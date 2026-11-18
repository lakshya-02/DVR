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

public static partial class DvrBuild
{
    private const string Root = "Assets/DVR";
    private const string ScenePath = Root + "/DVR.unity";
    private static readonly Color Ink = Hex(0x191515);
    private static readonly Color Case = Hex(0x44312C);
    private static readonly Color Paper = Hex(0xF5E0BD);
    private static readonly Color Amber = Hex(0xD99550);
    private static readonly Color Mint = Hex(0x96E1B5);

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
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Hex(0x7D6C61);
        BuildProjectionRoom(rig);

        ConfigureAndroid();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("DVR scene could not be saved.");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[DVR] Prototype scene assembled");
    }

    public static void RebuildAndroid()
    {
        Assemble();
        Android();
    }

    public static void Android()
    {
        BuildAndroid(false);
    }

    public static void RebuildWiredPilot()
    {
        Assemble();
        BuildAndroid(true);
    }

    private static void BuildAndroid(bool wiredPilot)
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
            options = wiredPilot ? BuildOptions.Development : BuildOptions.None,
            extraScriptingDefines = wiredPilot ? new[] { "DVR_WIRED_PILOT" } : Array.Empty<string>()
        });
        if (build.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Android build {build.summary.result} ({build.summary.totalErrors} errors)");
        Debug.Log($"[DVR] APK built at {output}");
    }

    private static void ConfigureAndroid()
    {
        ConfigureOpenXR();
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.graphicsJobs = false;
        PlayerSettings.companyName = "Lakshya";
        PlayerSettings.productName = "DVR";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.lakshya.dvr");
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

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
        var uv = new Vector2[vertices.Length];
        for (var i = 0; i < uv.Length; i++)
            uv[i] = new Vector2(vertices[i].x, vertices[i].z) / (2f * outer) + Vector2.one * 0.5f;
        mesh.uv = uv;
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

    private static Text Label(Transform parent, string text, float x, float y, float width, float height,
        int size, Color color, TextAnchor align, bool emphasis = false)
    {
        var label = Ui<Text>(parent, text, x, y, width, height);
        var weight = emphasis ? "Bold" : "Regular";
        label.font = AssetDatabase.LoadAssetAtPath<Font>($"{Root}/Typography/AtkinsonHyperlegible-{weight}.ttf");
        if (label.font == null) throw new InvalidOperationException($"Missing Atkinson Hyperlegible {weight} font.");
        label.text = text;
        label.fontSize = size;
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
        colors.highlightedColor = Hex(0x6F4F3B);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Hex(0x855634);
        colors.disabledColor = Hex(0x302523);
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
