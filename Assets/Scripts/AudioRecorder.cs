using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
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



        public string modelPath = "Model/silero_vad.onnx";
        public SamplingRate samplingRate;
        
        public float SilentTimeInterval = 2f;
        public float DetectThreshold = 0.5f;
        public int FixTrimLength = 1;

        public readonly int streamSampleCount = 1600;

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

            string model_path = Path.Combine(Application.streamingAssetsPath, modelPath);
            m_MICVADInference = new VADInference(model_path, SamplingRate.sr_16kHz, DetectThreshold, streamSampleCount);

           
        }


        // Start is called before the first frame update
        void Start()
        {
            m_AudioSource = GetComponent<AudioSource>();

            m_MICVADInference.SpeechEventHandler += this.OnSpeechEvent;

            activedDeciveName = AudioDeviceName;
            StartRecording();
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