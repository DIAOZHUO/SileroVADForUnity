using System;
using System.Collections;
using UnityEngine;


namespace UnityVAD
{
    /// <summary>
    /// Microphone -> Silero VAD -> speech segments.
    ///
    /// The microphone records into a looping ring buffer that is never restarted, so no audio is
    /// lost between utterances and recording does not stop after the clip length. Every frame all
    /// newly recorded 512-sample chunks are fed to a VADSegmenter, so a slow frame never makes the
    /// VAD fall behind.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioRecorder : MonoBehaviour
    {
        public static AudioRecorder Instance;

        /// Mono speech segment at samplingRate, raised SilentTimeInterval s after speech ends
        /// (or every MaxSegmentSeconds while speech continues).
        public VADUtil.EventHandler<float[]> MIC_DataAddedEventHandler;
        /// Same segments with their stream position and per-chunk VAD probabilities.
        public VADUtil.EventHandler<VADSegment> MIC_SegmentAddedEventHandler;
        public VADUtil.EventHandler<SpeechEvent> MIC_SpeechEventHandler
        {
            get { return m_MICVADInference.SpeechEventHandler; }
            set { m_MICVADInference.SpeechEventHandler = value; }
        }



        // The Silero VAD model, shipped inside the package as a .bytes (TextAsset) asset.
        // Assign silero_vad.bytes here on the prefab.
        public TextAsset modelAsset;
        public SamplingRate samplingRate;

        [Tooltip("Seconds of silence after speech that close a segment.")]
        public float SilentTimeInterval = 2f;
        [Tooltip("VAD speech probability that starts speech (ends below this - 0.15).")]
        public float DetectThreshold = 0.5f;
        [Tooltip("512-sample chunks of audio kept before the detected speech start.")]
        public int FixTrimLength = 1;
        [Tooltip("Emit a segment after this many seconds even if speech continues (0 = no limit).")]
        public float MaxSegmentSeconds = 0f;
        [Tooltip("Length of the looping microphone buffer. Must exceed the longest frame hitch.")]
        public int MicBufferSeconds = 10;

        // v5 silero_vad.onnx requires a fixed 512-sample hop at 16 kHz (VADInference.WIN).
        public readonly int streamSampleCount = VADInference.WIN;

        [HideInInspector]
        public AudioSource m_AudioSource;
        private VADInference m_MICVADInference;
        private VADSegmenter m_Segmenter;

        string activedDeciveName = null;
        string audioDeviceName = "";
        int readPosition;                                   // next unread sample in the ring buffer
        readonly float[] chunk = new float[VADInference.WIN];

        public string AudioDeviceName
        {
            get
            {
                if (0 <= Array.IndexOf(Microphone.devices, audioDeviceName))
                {
                    return audioDeviceName;
                }
                else if (Microphone.devices.Length > 0)
                {
                    return Microphone.devices[0];
                }
                else
                {
                    return null;
                }
            }

            private set
            {
                audioDeviceName = value;
            }
        }

        /// Underlying segmenter (e.g. to read SamplesProcessed or InSegment).
        public VADSegmenter Segmenter => m_Segmenter;


        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            m_MICVADInference = new VADInference(SamplingRate.sr_16kHz, DetectThreshold);
            m_Segmenter = new VADSegmenter(m_MICVADInference, (int)samplingRate);
            m_Segmenter.SegmentReady += seg =>
            {
                MIC_DataAddedEventHandler?.Invoke(seg.Audio);
                MIC_SegmentAddedEventHandler?.Invoke(seg);
            };

            // The model ships inside the package as a .bytes TextAsset (assigned on the prefab),
            // so it loads straight from memory on every platform -- no StreamingAssets, no
            // UnityWebRequest, no file-path juggling (and it works inside a UPM package).
            if (modelAsset != null && modelAsset.bytes != null && modelAsset.bytes.Length > 0)
            {
                m_MICVADInference.LoadModel(modelAsset.bytes);
            }
            else
            {
                Debug.LogError("[VAD] modelAsset is not assigned on AudioRecorder; VAD will not run. " +
                               "Assign silero_vad.bytes to the Model Asset field.");
            }
        }


        IEnumerator Start()
        {
            m_AudioSource = GetComponent<AudioSource>();

            // On Android (Quest) RECORD_AUDIO is a runtime permission. Request it and wait for
            // the result before opening the mic -- otherwise Microphone.devices is empty and
            // Microphone.Start returns a clip that never receives samples.
            yield return RequestMicrophonePermission();

            StartRecording(AudioDeviceName);
        }

        IEnumerator RequestMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);

                float waited = 0f;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) && waited < 30f)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                {
                    Debug.LogWarning("[VAD] microphone permission was not granted; recording will not work.");
                }
            }
#endif
            yield return null;
        }

        public void SetAudioRecordDevice(string deviceName)
        {
            AudioDeviceName = deviceName;
            StartRecording(AudioDeviceName);
        }

        void StartRecording(string device)
        {
            StopRecording();
            if (device == null)
            {
                Debug.Log("no audio device...");
                return;
            }

            // new stream: forget the previous device's VAD state
            m_Segmenter.Reset();
            readPosition = 0;
            m_AudioSource.clip = Microphone.Start(device, true, Mathf.Max(2, MicBufferSeconds), (int)samplingRate);
            activedDeciveName = device;
        }

        void StopRecording()
        {
            if (activedDeciveName == null) return;
            m_Segmenter.Flush();                    // report speech that was still open
            Microphone.End(activedDeciveName);
            activedDeciveName = null;
        }

        void OnDisable()
        {
            StopRecording();
        }

        void OnEnable()
        {
            // resume after a disable (Start handles the first time)
            if (m_AudioSource != null && activedDeciveName == null) StartRecording(AudioDeviceName);
        }


        void Update()
        {
            if (activedDeciveName == null || m_AudioSource == null || m_AudioSource.clip == null) return;

            m_MICVADInference.threshold = DetectThreshold;
            m_Segmenter.SilentTimeInterval = SilentTimeInterval;
            m_Segmenter.FixTrimLength = FixTrimLength;
            m_Segmenter.MaxSegmentSeconds = MaxSegmentSeconds;

            var clip = m_AudioSource.clip;
            int position = Microphone.GetPosition(activedDeciveName);
            if (position < 0) return;
            int available = (position - readPosition + clip.samples) % clip.samples;

            // process every complete chunk recorded since the last frame
            while (available >= streamSampleCount)
            {
                ReadRing(clip, readPosition, chunk);
                readPosition = (readPosition + streamSampleCount) % clip.samples;
                available -= streamSampleCount;
                m_Segmenter.Process(chunk);
            }
        }

        static float[] ringTail = new float[0], ringHead = new float[0];

        static void ReadRing(AudioClip clip, int start, float[] dst)
        {
            int first = Mathf.Min(dst.Length, clip.samples - start);
            if (first == dst.Length)
            {
                clip.GetData(dst, start);
                return;
            }
            // chunk wraps around the end of the ring buffer
            if (ringTail.Length != first) ringTail = new float[first];
            if (ringHead.Length != dst.Length - first) ringHead = new float[dst.Length - first];
            clip.GetData(ringTail, start);
            clip.GetData(ringHead, 0);
            Array.Copy(ringTail, 0, dst, 0, first);
            Array.Copy(ringHead, 0, dst, first, ringHead.Length);
        }
    }

}
