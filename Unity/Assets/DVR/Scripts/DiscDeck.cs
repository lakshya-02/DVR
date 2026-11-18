using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace DVR
{
    public sealed partial class DiscDeck : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Transform station;
        [SerializeField] private Transform stand;
        [SerializeField] private Transform slot;
        [SerializeField] private Rigidbody disc;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private GameObject[] grabInteractions;
        [SerializeField] private GameObject channelPanel;
        [SerializeField] private Button[] rows;
        [SerializeField] private Button randomButton;
        [SerializeField] private Button demoButton;
        [SerializeField] private Text status;
        [SerializeField] private Text deckStatus;
        [SerializeField] private Television television;

        private readonly HashSet<int> pointers = new();
        private readonly System.Random random = new();
        private IReadOnlyList<ChannelCatalog.Channel> channels;
        private int selected = -1;
        private bool placed;
        private bool seated;
        private bool checkRelease;

        private void Start()
        {
            channels = ChannelCatalog.Create();
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                row.gameObject.SetActive(index < channels.Count);
                if (index >= channels.Count) continue;
                row.GetComponentInChildren<Text>().text = channels[index].Name;
                var channelIndex = index;
                row.onClick.AddListener(() => SelectChannel(channelIndex));
            }

            randomButton.interactable = channels.Count > 0;
            demoButton.gameObject.SetActive(television.HasDemo);
            channelPanel.SetActive(false);
            station.gameObject.SetActive(false);
            grabbable.WhenPointerEventRaised += OnDiscEvent;
#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
            RunRequestedDeviceProbe();
#endif
        }

        private void LateUpdate()
        {
            if (!placed && HeadReady()) Recenter();

            if (!checkRelease) return;

            checkRelease = false;
            if (seated || pointers.Count != 0) return;

            var offset = slot.InverseTransformPoint(disc.transform.position);
            if (Mathf.Abs(offset.x) <= 0.11f && Mathf.Abs(offset.y) <= 0.08f
                && Mathf.Abs(offset.z) <= 0.13f)
                Insert();
            else
                ParkDisc(stand);
        }

        private bool HeadReady()
        {
#if UNITY_EDITOR
            if (!XRSettings.isDeviceActive) return true;
#endif
            return OVRManager.hasInputFocus && (OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter)
                || head.localPosition.sqrMagnitude > 0.1f);
        }

        private void OnDiscEvent(PointerEvent interaction)
        {
            switch (interaction.Type)
            {
                case PointerEventType.Select:
                    pointers.Add(interaction.Identifier);
                    checkRelease = false;
                    break;
                case PointerEventType.Unselect:
                    checkRelease = pointers.Remove(interaction.Identifier);
                    break;
                case PointerEventType.Cancel:
                    if (pointers.Remove(interaction.Identifier) && pointers.Count == 0)
                        ParkDisc(stand);
                    checkRelease = false;
                    break;
            }
        }

        public void Recenter()
        {
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.Cross(head.right, Vector3.up);

            if (pointers.Count > 0) return;
            station.SetPositionAndRotation(head.position, Quaternion.LookRotation(forward, Vector3.up));
            ParkDisc(seated ? slot : stand);
            placed = true;
            station.gameObject.SetActive(true);
            Debug.Log("[DVR] Station placed");
        }

        private void Insert()
        {
            seated = true;
            foreach (var interaction in grabInteractions) interaction.SetActive(false);
            ParkDisc(slot);
            deckStatus.text = "DISC LOADED";
            status.text = channels.Count == 0 ? "No channels available." : "Choose a live channel.";
            channelPanel.SetActive(true);
            Debug.Log("[DVR] Disc inserted");
        }

        public void Eject()
        {
            if (!seated) return;

            television.Stop();
            selected = -1;
            seated = false;
            channelPanel.SetActive(false);
            ParkDisc(stand);
            foreach (var interaction in grabInteractions) interaction.SetActive(true);
            deckStatus.text = "INSERT DISC";
            Debug.Log("[DVR] Disc ejected");
        }

        private void ParkDisc(Transform position)
        {
            disc.transform.SetPositionAndRotation(position.position, position.rotation);
            disc.position = position.position;
            disc.rotation = position.rotation;
        }

        private void SelectChannel(int index)
        {
            if (!seated || index < 0 || index >= channels.Count) return;

            selected = index;
            var channel = channels[index];
            Debug.Log($"[DVR] Selected {channel.Name}");
            television.Play(channel.Url, message => status.text = $"{channel.Name}\n{message}");
        }

        public void RandomChannel() => SelectChannel(ChannelCatalog.PickOther(channels.Count, selected, random));

        public void PlayDemo()
        {
            if (!seated) return;
            selected = -1;
            television.PlayDemo(message => status.text = $"Offline demo\n{message}");
        }

        private void OnDestroy()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised -= OnDiscEvent;
        }
    }
}
