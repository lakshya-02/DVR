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
        var screen = Place("Television", station, new Vector3(-0.30f, 0.06f, 3.02f));
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
        var console = Place("Projection console", station, new Vector3(0, -0.36f, 0.435f));
        var wood = Finish("Walnut", 0xAA6842, 0.03f, 0.30f, Grain());
        var brass = Finish("Brushed bronze", 0x9D714A, 0.42f, 0.37f);
        var shell = Finish("Black lacquer", 0x212020, 0.19f, 0.27f);
        var graphite = Finish("Graphite", 0x3A3431, 0.11f, 0.20f);
        var felt = Finish("Rack cork", 0x443029, 0, 0.13f);
        Profile(console, "Solid walnut lid", new Vector3(0, -0.089f, 0), new Vector3(0.94f, 0.067f, 0.51f), wood);
        Profile(console, "Bronze seam", new Vector3(0, -0.115f, -0.001f), new Vector3(0.93f, 0.010f, 0.505f), brass);
        Profile(console, "Console plinth", new Vector3(0, -0.173f, 0.017f), new Vector3(0.89f, 0.106f, 0.466f), graphite);
        for (var i = -1; i <= 1; i += 2)
        {
            Profile(console, "Drawer face", new Vector3(i * 0.224f, -0.163f, -0.223f), new Vector3(0.428f, 0.077f, 0.026f), wood);
            Profile(console, "Drawer pull", new Vector3(i * 0.224f, -0.151f, -0.244f), new Vector3(0.11f, 0.011f, 0.022f), brass);
            var leg = Profile(console, "Tapered support", new Vector3(i * 0.362f, -0.385f, 0.01f),
                new Vector3(0.063f, 0.39f, 0.32f), wood);
            leg.localRotation = Quaternion.Euler(0, 0, 5 * i);
        }

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
        links.eject.GetComponentInChildren<Text>().fontSize = 19;
        QuickActionsAPI.AddRayCanvasInteraction(face.gameObject);

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
        var housing = Place("Source panel enclosure", station, new Vector3(1.24f, 0.005f, 1.30f));
        housing.localRotation = Quaternion.Euler(0, 40f, 0);
        Profile(housing, "Anodized edge", new Vector3(0, 0, 0.013f), new Vector3(0.462f, 0.591f, 0.023f),
            Finish("Brushed bronze", 0x9D714A, 0.42f, 0.37f));
        Profile(housing, "Glass back", new Vector3(0, 0, 0.004f), new Vector3(0.451f, 0.582f, 0.019f),
            Finish("Smoked glass", 0x221D1C, 0.10f, 0.60f));
        var panel = MakeCanvas("Source controls", housing, camera, new Vector3(0, 0, -0.008f), new Vector2(442, 570), 0.001f);
        links.sourcePanel = housing.gameObject;
        ImageAt(panel.transform, "Glass surface", 0, 0, 442, 570, new Color(0.09f, 0.071f, 0.061f, 0.79f));
        ImageAt(panel.transform, "Reflected light", 0, 283, 428, 2, Hex(0x98715A));
        ImageAt(panel.transform, "Title rule", 0, 182, 391, 2, Amber);
        links.title = Label(panel.transform, "Disc library", 0, 218, 394, 57, 31, Paper, TextAnchor.MiddleLeft);
        links.status = Label(panel.transform, "", 0, 128, 394, 88, 21, Paper, TextAnchor.MiddleLeft);
        links.rows = new Button[5];
        for (var i = 0; i < links.rows.Length; i++)
        {
            var row = MakeButton(panel.transform, "", 0, 48 - i * 45, 394, 42);
            var label = row.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.sizeDelta = new Vector2(360, 39);
            label.fontSize = 21;
            links.rows[i] = row;
        }

        links.previous = MakeButton(panel.transform, "Previous", -153, -197, 91, 46);
        links.next = MakeButton(panel.transform, "Next", -51, -197, 91, 46);
        links.random = MakeButton(panel.transform, "Random channel", 101, -197, 191, 46);
        links.search = MakeButton(panel.transform, "Search", 51, -197, 91, 46);
        links.back = MakeButton(panel.transform, "Settings", 153, -197, 91, 46);
        links.netflix = MakeButton(panel.transform, "Open Netflix", -101, -252, 191, 47);
        links.prime = MakeButton(panel.transform, "Open Prime Video", 101, -252, 191, 47);
        links.qr = Ui<RawImage>(panel.transform, "Drive pairing QR", 61, -74, 149, 149);
        links.qr.raycastTarget = false;
        links.qr.gameObject.SetActive(false);
        QuickActionsAPI.AddRayCanvasInteraction(panel.gameObject);
    }

    private static void MakeControls(Transform station, Camera camera, DiscDeck.SceneLinks links)
    {
        var controls = MakeCanvas("Session controls", station, camera, new Vector3(-0.12f, -0.645f, 1.20f),
            new Vector2(968, 146), 0.001f);
        ImageAt(controls.transform, "Control plate", 0, 0, 968, 146, Ink);
        ImageAt(controls.transform, "Bronze edge", 0, 72, 968, 2, Amber);
        links.footer = Label(controls.transform, "Grip a disc and place it on the tray.", -134, 46, 660, 32, 23, Paper, TextAnchor.MiddleLeft);
        links.volumeLabel = Label(controls.transform, "100%", 323, 46, 83, 32, 21, Amber, TextAnchor.MiddleCenter);
        links.panelEject = MakeButton(controls.transform, "Eject", 421, 46, 108, 40);
        links.room = MakeButton(controls.transform, "Theater", -389, -17, 150, 58);
        links.recenter = MakeButton(controls.transform, "Recenter", -230, -17, 143, 58);
        links.move = MakeButton(controls.transform, "Move screen", -75, -17, 151, 58);
        links.rewind = MakeButton(controls.transform, "−10s", 40, -17, 65, 58);
        links.pause = MakeButton(controls.transform, "Pause / Play", 140, -17, 126, 58);
        links.forward = MakeButton(controls.transform, "+10s", 240, -17, 65, 58);
        links.quieter = MakeButton(controls.transform, "Vol −", 312, -17, 65, 58);
        links.louder = MakeButton(controls.transform, "Vol +", 384, -17, 65, 58);
        links.hide = MakeButton(controls.transform, "Hide", 448, -17, 53, 58);
        links.hide.GetComponentInChildren<Text>().fontSize = 19;
        QuickActionsAPI.AddRayCanvasInteraction(controls.gameObject);

    }
}
