using System;
using DVR;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public static partial class DvrBuild
{
    private static void BuildProjectionRoom(OVRCameraRig rig)
    {
        var station = Place("Projection Room", null, Vector3.zero);
        var camera = rig.centerEyeAnchor.GetComponent<Camera>();
        var links = new DiscDeck.SceneLinks
        {
            station = station,
            head = rig.centerEyeAnchor,
            leftController = rig.leftControllerAnchor,
            rightController = rig.rightControllerAnchor,
        };
        MakeScreen(station, camera, links);
        MakeConsole(station, camera, links);
        MakePanel(station, camera, links);
        MakeControls(station, camera, links);

        var session = new GameObject("Projection room session");
        links.personal = session.AddComponent<PersonalDrivePanel>();
        session.AddComponent<DiscDeck>().Configure(links);
    }

    private static void MakeScreen(Transform station, Camera camera, DiscDeck.SceneLinks links)
    {
        var screen = Place("Television", station, new Vector3(0, 0.50f, 3.02f));
        links.screen = screen;
        var shell = Finish("Black lacquer", 0x212020, 0.19f, 0.27f);
        var brass = Finish("Brushed bronze", 0x9D714A, 0.42f, 0.37f);
        Profile(screen, "Slim screen frame", new Vector3(0, 0, 0.012f), new Vector3(1.055f, 0.601f, 0.029f), shell);
        Profile(screen, "Bronze surround", new Vector3(0, 0, -0.003f), new Vector3(1.057f, 0.602f, 0.015f), brass);
        Profile(screen, "Light absorbing border", new Vector3(0, 0, -0.009f), new Vector3(1.052f, 0.597f, 0.009f), shell);
        var display = MakeCanvas("Display", screen, camera, new Vector3(0, 0, -0.014f), new Vector2(1040, 585), 0.001f);
        ImageAt(display.transform, "Screen black", 0, 0, 1040, 585, Hex(0x100E0D));
        links.screenStatus = Label(display.transform, "Insert a disc", 0, 0, 910, 130, 36, Amber, TextAnchor.MiddleCenter);
        var picture = Ui<RawImage>(display.transform, "Picture", 0, 0, 1040, 585);
        picture.raycastTarget = false;
        links.television = screen.gameObject.AddComponent<Television>();
        var tv = new SerializedObject(links.television);
        Set(tv, "picture", picture);
        Set(tv, "demoClip", AssetDatabase.LoadAssetAtPath<VideoClip>(Root + "/Media/OfflineDemo.mp4"));
        tv.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void MakeConsole(Transform station, Camera camera, DiscDeck.SceneLinks links)
    {
        var console = Place("Projection shelf", station, new Vector3(0, -0.38f, 0.435f));
        links.shelf = console;
        var wood = Finish("Walnut", 0xAA6842, 0.03f, 0.30f, Grain());
        var brass = Finish("Brushed bronze", 0x9D714A, 0.42f, 0.37f);
        var shell = Finish("Black lacquer", 0x212020, 0.19f, 0.27f);
        var graphite = Finish("Graphite", 0x3A3431, 0.11f, 0.20f);
        var felt = Finish("Rack cork", 0x443029, 0, 0.13f);
        Profile(console, "Walnut shelf", new Vector3(0, -0.082f, 0), new Vector3(0.94f, 0.047f, 0.51f), wood);
        Profile(console, "Bronze seam", new Vector3(0, -0.098f, -0.001f), new Vector3(0.93f, 0.009f, 0.505f), brass);

        var player = Place("Optical disc deck", console, new Vector3(0, 0, 0.064f));
        Profile(player, "Die-cast shell", new Vector3(0, 0.019f, 0.069f), new Vector3(0.346f, 0.128f, 0.293f), shell);
        Profile(player, "Smoked top cover", new Vector3(0, 0.084f, 0.078f), new Vector3(0.335f, 0.017f, 0.274f), graphite);
        Profile(player, "Machined fascia", new Vector3(0, 0.009f, -0.084f), new Vector3(0.331f, 0.104f, 0.026f), graphite);
        Profile(player, "Disc opening", new Vector3(-0.026f, 0.025f, -0.100f), new Vector3(0.190f, 0.028f, 0.012f), shell);
        Profile(player, "Open disc tray", new Vector3(-0.026f, 0.012f, -0.174f), new Vector3(0.190f, 0.020f, 0.165f), graphite);
        Profile(player, "Tray pull", new Vector3(-0.026f, 0.011f, -0.255f), new Vector3(0.173f, 0.013f, 0.015f), brass);
        var well = Profile(player, "Outer tray guides", new Vector3(-0.026f, 0.026f, -0.168f), new Vector3(0.148f, 0.003f, 0.148f), brass);
        var socketMesh = RingMesh("Optical well", 0.062f, 0.072f, 0.002f);
        well.GetComponent<MeshFilter>().sharedMesh = socketMesh;
        well.localScale = Vector3.one;
        var socket = MeshAt(player, "Spindle", new Vector3(-0.026f, 0.027f, -0.168f),
            RingMesh("Optical spindle", 0.004f, 0.016f, 0.005f), brass);
        links.slot = Place("Disc latch", player, socket.localPosition + Vector3.up * 0.005f);

        for (var i = 0; i < 9; i++)
            Box(player, "Cooling vent", new Vector3(-0.105f + i * 0.026f, 0.093f, 0.15f), new Vector3(0.012f, 0.001f, 0.028f), shell);
        var face = MakeCanvas("Deck fascia controls", player, camera, new Vector3(0, 0.008f, -0.108f), new Vector2(330, 96), 0.001f);
        Label(face.transform, "D V R", -101, -21, 120, 24, 19, Paper, TextAnchor.MiddleLeft);
        links.readyLight = ImageAt(face.transform, "Transport light", -25, -20, 11, 5, Amber);
        Label(face.transform, "X  Recenter", -104, 11, 106, 22, 15, Paper, TextAnchor.MiddleLeft);
        links.reveal = MakeButton(face.transform, "Controls", 26, 4, 97, 54);
        links.eject = MakeButton(face.transform, "Eject", 111, 4, 69, 54);
        links.reveal.GetComponentInChildren<Text>().fontSize = 19;
        links.eject.GetComponentInChildren<Text>().fontSize = 20;
        StyleEject(links.eject);
        QuickActionsAPI.AddPokeCanvasInteraction(face.gameObject);

        links.mechanicalAudio = player.gameObject.AddComponent<AudioSource>();
        links.mechanicalAudio.clip = MechanicalClick();
        links.mechanicalAudio.playOnAwake = false;
        links.mechanicalAudio.spatialBlend = 0.8f;
        links.mechanicalAudio.volume = 0.44f;
        links.mechanicalAudio.minDistance = 0.3f;
        links.mechanicalAudio.maxDistance = 1.5f;

        string[] ids = { "offline", "feature", "personal", "sports", "indian", "news" };
        if (ChannelCatalog.Discs.Count != ids.Length)
            throw new InvalidOperationException("The mounting positions must match the media catalog.");
        links.discs = new DiscDeck.DiscObject[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            var discIndex = i;
            var media = System.Linq.Enumerable.Single(ChannelCatalog.Discs, item => item.Id == ids[discIndex]);
            var x = i < 3 ? -0.33f : 0.33f;
            var z = -0.158f + (i % 3) * 0.158f;
            var rest = Place(media.Name + " cradle", console, new Vector3(x, -0.054f, z));
            Profile(rest, "Recessed cork cradle", Vector3.zero, new Vector3(0.162f, 0.009f, 0.151f), felt);
            Profile(rest, "Rear riser", new Vector3(0, 0.010f, 0.045f), new Vector3(0.106f, 0.024f, 0.042f), graphite);
            var home = Place("Disc position", rest, new Vector3(0, 0.029f, 0));
            home.localRotation = Quaternion.Euler(-20f, i < 3 ? 9f : -9f, 0);
            links.discs[i] = MakeDisc(console, home, media);
        }

        AddLight(station, "Console key", LightType.Point, new Vector3(-0.52f, 0.44f, 0.42f),
            Hex(0xFFE0B7), 1.20f, 2.7f);
    }

    private static DiscDeck.DiscObject MakeDisc(Transform parent, Transform home, ChannelCatalog.Disc media)
    {
        var disc = Place(media.Name + " disc", parent, Vector3.zero);
        MeshAt(disc, "Polycarbonate", Vector3.zero, RingMesh("Optical profile", 0.011f, 0.073f, 0.005f),
            Finish("Optical polycarbonate", 0xADB1AD, 0.30f, 0.65f));
        MeshAt(disc, "Printed artwork", new Vector3(0, 0.0033f, 0), RingMesh("Optical label", 0.014f, 0.071f, 0.0005f),
            Finish("Print " + media.Id, 0xFFFFFF, 0.02f, 0.19f, DiscArt(media.Id)));
        MeshAt(disc, "Mirrored hub", new Vector3(0, 0.0035f, 0), RingMesh("Optical hub", 0.011f, 0.018f, 0.0004f),
            Finish("Optical polycarbonate", 0xADB1AD, 0.30f, 0.65f));
        var print = MakeCanvas("Printed title", disc, null, new Vector3(0, 0.004f, -0.035f),
            new Vector2(236, 67), 0.00043f);
        print.transform.localRotation = Quaternion.Euler(90f, 0, 0);
        ImageAt(print.transform, "Label field", 0, 0, 236, 67, new Color(0.051f, 0.041f, 0.04f, 0.89f));
        Label(print.transform, media.Name, 0, 0, 226, 63,
            media.Name.Length > 19 ? 24 : 29, Paper, TextAnchor.MiddleCenter);
        var collider = disc.gameObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.145f, 0.024f, 0.145f);
        disc.SetPositionAndRotation(home.position, home.rotation);
        var interactions = QuickActionsAPI.AddGrabInteraction(disc.gameObject).ToArray();
        var body = disc.GetComponent<Rigidbody>();
        var grabbable = disc.GetComponent<Grabbable>();
        if (body == null || grabbable == null || interactions.Length == 0)
            throw new InvalidOperationException($"Grab template could not configure {media.Name}.");
        grabbable.MaxGrabPoints = 1;
        return new DiscDeck.DiscObject { catalogId = media.Id, home = home, body = body, grabbable = grabbable, interactions = interactions };
    }

    private static void MakePanel(Transform station, Camera camera, DiscDeck.SceneLinks links)
    {
        var housing = Place("Disc and source panel", station, new Vector3(0, -0.599f, 1.22f));
        Profile(housing, "Anodized edge", new Vector3(0, 0, 0.011f), new Vector3(1.061f, 0.253f, 0.017f),
            Finish("Brushed bronze", 0x9D714A, 0.42f, 0.37f));
        var panel = MakeCanvas("Source controls", housing, camera, Vector3.zero, new Vector2(1056, 250), 0.001f);
        links.sourcePanel = housing.gameObject;
        ImageAt(panel.transform, "Glass surface", 0, 0, 1056, 250, Hex(0x181B21));
        ImageAt(panel.transform, "Identity edge", -176, 0, 2, 214, Hex(0x695141));
        links.title = Label(panel.transform, "Disc library", -348, 87, 320, 42, 30, Paper, TextAnchor.MiddleLeft, emphasis: true);
        links.status = Label(panel.transform, "", -348, -17, 320, 84, 22, Paper, TextAnchor.MiddleLeft);

        var identity = ImageAt(panel.transform, "Google Drive account", -348, 40, 323, 52, Hex(0x263040));
        links.driveIdentity = identity.gameObject;
        var mark = Ui<RawImage>(identity.transform, "Official Google Drive logo", -136, 0, 37, 37);
        mark.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Media/GoogleDrive.png");
        mark.raycastTarget = false;
        if (mark.texture == null) throw new InvalidOperationException("Google Drive's official logo asset is missing.");
        links.driveAccount = Label(identity.transform, "Google Drive", 23, 0, 271, 51, 19, Color.white, TextAnchor.MiddleLeft);
        links.driveAccount.resizeTextForBestFit = true;
        links.driveAccount.resizeTextMinSize = 16;
        links.driveAccount.resizeTextMaxSize = 19;
        links.driveIdentity.SetActive(false);

        links.discButtons = new Button[links.discs.Length];
        for (var i = 0; i < links.discs.Length; i++)
        {
            var index = i;
            var media = System.Linq.Enumerable.Single(ChannelCatalog.Discs, item => item.Id == links.discs[index].catalogId);
            links.discButtons[i] = MakeSourceTile(panel.transform, media.Name, i);
        }

        links.rows = new Button[5];
        for (var i = 0; i < links.rows.Length; i++)
            links.rows[i] = MakeSourceTile(panel.transform, "", i);

        links.previous = MakeButton(panel.transform, "Previous", -109, -94, 98, 48);
        links.next = MakeButton(panel.transform, "Next", -2, -94, 98, 48);
        links.random = MakeButton(panel.transform, "Random channel", 157, -94, 206, 48);
        links.search = MakeButton(panel.transform, "Search", 105, -94, 98, 48);
        links.back = MakeButton(panel.transform, "Settings", 218, -94, 112, 48);
        links.driveLibrary = MakeButton(panel.transform, "Folders", 365, -94, 165, 48);
        links.netflix = MakeButton(panel.transform, "Open Netflix", -431, -94, 153, 48);
        links.prime = MakeButton(panel.transform, "Open Prime Video", -266, -94, 163, 48);
        links.prime.GetComponentInChildren<Text>().fontSize = 19;
        links.qr = Ui<RawImage>(panel.transform, "Drive pairing QR", 406, 4, 157, 157);
        links.qr.raycastTarget = false;
        links.qr.gameObject.SetActive(false);
        UseControlOverlay(panel);
        QuickActionsAPI.AddRayCanvasInteraction(panel.gameObject);
    }

    private static Button MakeSourceTile(Transform panel, string label, int index)
    {
        var row = MakeButton(panel, label, 5 + 336 * (index % 2), 79 - (index / 2) * 55, 320, 48);
        var text = row.GetComponentInChildren<Text>();
        text.alignment = TextAnchor.MiddleLeft;
        text.rectTransform.sizeDelta = new Vector2(283, 42);
        text.fontSize = 24;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 19;
        text.resizeTextMaxSize = 24;
        return row;
    }

    private static void MakeControls(Transform station, Camera camera, DiscDeck.SceneLinks links)
    {
        var controls = MakeCanvas("Transport and room controls", station, camera, new Vector3(0, -0.39f, 1.22f),
            new Vector2(1056, 148), 0.001f);
        ImageAt(controls.transform, "Control plate", 0, 0, 1056, 148, new Color32(24, 27, 33, 220));
        ImageAt(controls.transform, "Bronze edge", 0, 73, 1056, 2, Amber);
        links.discLibrary = MakeButton(controls.transform, "Discs", -434, 36, 142, 54);
        links.room = MakeButton(controls.transform, "Theater", -276, 36, 144, 54);
        links.recenter = MakeButton(controls.transform, "Recenter", -118, 36, 144, 54);
        links.move = MakeButton(controls.transform, "Screen position", 46, 36, 160, 54);
        links.deckHeight = MakeButton(controls.transform, "Deck height", 205, 36, 143, 54);
        links.hide = MakeButton(controls.transform, "Hide", 334, 36, 91, 54);
        links.panelEject = MakeButton(controls.transform, "Eject", 448, 36, 115, 54);
        StyleEject(links.panelEject);
        links.move.GetComponentInChildren<Text>().fontSize = 21;
        links.footer = Label(controls.transform, "Pick a disc with the ray or place it on the tray.", -275, -34, 451, 63, 21, Paper, TextAnchor.MiddleLeft);
        links.rewind = MakeButton(controls.transform, "−10s", 3, -34, 83, 53);
        links.pause = MakeButton(controls.transform, "Pause / Play", 112, -34, 113, 53);
        links.pause.GetComponentInChildren<Text>().fontSize = 19;
        links.forward = MakeButton(controls.transform, "+10s", 221, -34, 83, 53);
        links.quieter = MakeButton(controls.transform, "Vol −", 325, -34, 83, 53);
        links.volumeLabel = Label(controls.transform, "100%", 395, -34, 53, 48, 20, Paper, TextAnchor.MiddleCenter);
        links.louder = MakeButton(controls.transform, "Vol +", 464, -34, 83, 53);
        UseControlOverlay(controls);
        QuickActionsAPI.AddRayCanvasInteraction(controls.gameObject);

        var restore = MakeCanvas("Restore controls", station, camera, new Vector3(0, -0.39f, 1.22f),
            new Vector2(198, 55), 0.001f);
        links.floatingReveal = MakeButton(restore.transform, "Show controls", 0, 0, 188, 53);
        UseControlOverlay(restore);
        QuickActionsAPI.AddRayCanvasInteraction(restore.gameObject);
        restore.gameObject.SetActive(false);
    }

    private static void UseControlOverlay(Canvas canvas)
    {
        var path = AssetPath("Floating controls", "mat");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/EditorSupport/VisibleControls.shader");
            if (shader == null) throw new InvalidOperationException("Missing floating controls shader.");
            material = new Material(shader) { name = "Floating controls" };
            AssetDatabase.CreateAsset(material, path);
        }

        foreach (var graphic in canvas.GetComponentsInChildren<Graphic>(true))
            graphic.material = material;
    }

    private static void StyleEject(Button button)
    {
        var colors = button.colors;
        colors.normalColor = Hex(0xB6433E);
        colors.highlightedColor = Hex(0xD85851);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Hex(0x882E2A);
        colors.disabledColor = Hex(0x2B2526);
        button.colors = colors;
        button.GetComponentInChildren<Text>().color = Color.white;
    }
}
