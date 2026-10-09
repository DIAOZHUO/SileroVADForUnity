using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;


namespace UnityVAD
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioRecorder : MonoBehaviour
    {
        public static AudioRecorder Instance;
        
        public VADUtil.EventHandler<float[]> MIC_DataAddedEventHandler;
        public VADUtil.EventHandler<SpeechEvent> MIC_SpeechEventHandler
        {
            get { return m_MICVADInference.SpeechEventHandler; }
            set { m_MICVADInference.SpeechEventHandler = value; }
        }



        // The Silero VAD model, shipped inside the package as a .bytes (TextAsset) asset.
        // Assign silero_vad.bytes here on the prefab.
        public TextAsset modelAsset;
        public SamplingRate samplingRate;
        
        public float SilentTimeInterval = 2f;
        public float DetectThreshold = 0.5f;
        public int FixTrimLength = 1;

        // v5 silero_vad.onnx requires a fixed 512-sample hop at 16 kHz (VADInference.WIN).
        public readonly int streamSampleCount = VADInference.WIN;

        [HideInInspector]
        public AudioSource m_AudioSource;
        private VADInference m_MICVADInference;

#if UNITY_EDITOR_WIN
        
#endif

        int currentSample = 0;
        int startSampleIdx;
        int endSampleIdx;

        bool speechStart = false;
        bool speechEndTrigger = false;
        float speechEndDurationCounter = 0f;

        string activedDeciveName = "";
        string audioDeviceName = "";
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



        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            m_MICVADInference = new VADInference(SamplingRate.sr_16kHz, DetectThreshold);

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


        // Start is called before the first frame update
        IEnumerator Start()
        {
            m_AudioSource = GetComponent<AudioSource>();

            m_MICVADInference.SpeechEventHandler += this.OnSpeechEvent;

            // On Android (Quest) RECORD_AUDIO is a runtime permission. Request it and wait for
            // the result before opening the mic -- otherwise Microphone.devices is empty and
            // Microphone.Start returns a clip that never receives samples.
            yield return RequestMicrophonePermission();

            activedDeciveName = AudioDeviceName;
            StartRecording();
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
            Microphone.End(AudioDeviceName);
            AudioDeviceName = deviceName;
            StartRecording();
        }

        void StartRecording()
        {
            if (activedDeciveName == null)
            {
                Debug.Log("no audio device...");
                return;
            }

            if (speechStart && (endSampleIdx - startSampleIdx) > 3 * streamSampleCount)
            {
                MIC_DataAddedEventHandler?.Invoke(GetRecordingData(startSampleIdx, endSampleIdx));
            }

            //Debug.Log("start recording");
            currentSample = 0;
            speechEndDurationCounter = 0f;

            speechStart = false;
            speechEndTrigger = false;

            Microphone.End(activedDeciveName);

            m_AudioSource.clip = Microphone.Start(AudioDeviceName, false, 1919, (int)samplingRate);

            activedDeciveName = AudioDeviceName;
        }

        float[] GetRecordingData(int start, int end)
        {
            return GetRecordingDataFromDuration(start, end - start);
        }

        float[] GetRecordingDataFromDuration(int start, int duration)
        {
            var samplesData = new float[m_AudioSource.clip.samples * m_AudioSource.clip.channels];
            m_AudioSource.clip.GetData(samplesData, 0);
            return samplesData.Skip(start).Take(duration).ToArray();
        }


        // Update is called once per frame
        void Update()
        {
            if (activedDeciveName != null)
            {

                var sampleIdx = Microphone.GetPosition(activedDeciveName);
                if (sampleIdx >= currentSample + streamSampleCount)
                {
                    var samplesData = new float[m_AudioSource.clip.samples * m_AudioSource.clip.channels];
                    m_AudioSource.clip.GetData(samplesData, 0);

                    var samples = GetRecordingDataFromDuration(currentSample, streamSampleCount);

                    m_MICVADInference.UpdateSpeechData(samples);
                    currentSample += streamSampleCount;
                }


                if (speechStart && speechEndTrigger)
                {

                    speechEndDurationCounter += Time.deltaTime;

                    if (speechEndDurationCounter >= SilentTimeInterval)
                    {
                        StartRecording();
                    }

                }
            }

        }

        void OnSpeechEvent(SpeechEvent speechEvent)
        {

            switch (speechEvent)
            {
                case SpeechEvent.Start:
                    if (!speechStart)
                    {
                        startSampleIdx = Mathf.Max(0, currentSample - streamSampleCount * FixTrimLength);
                    }

                    speechStart = true;
                    speechEndDurationCounter = 0f;
                    
                    break;

                case SpeechEvent.End:
                    endSampleIdx = currentSample;

                    speechEndTrigger = true;

                    break;
            }


        }
    }

}