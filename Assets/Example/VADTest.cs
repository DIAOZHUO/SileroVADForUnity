using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityVAD;

[RequireComponent(typeof(AudioSource))]
public class VADTest : MonoBehaviour
{
    
    public Image speakingImage;
    public Text text;
    public Dropdown audioDeviceDropdown;

    public bool saveAudioFile = false;

    private AudioRecorder audioRecorder;
    private AudioClip record_clip;
    private AudioSource audioSource;

    // Start is called before the first frame update
    void Start()
    {
        audioSource = GetComponent<AudioSource>();

        audioRecorder = AudioRecorder.Instance;

        audioRecorder.MIC_SpeechEventHandler += OnMicSpeechEvent;
        audioRecorder.MIC_DataAddedEventHandler += OnMicDataAddedEvent;

        audioDeviceDropdown.ClearOptions();
        audioDeviceDropdown.AddOptions(new List<string>(Microphone.devices));
    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnMicSpeechEvent(SpeechEvent speechEvent)
    {
        Debug.Log(speechEvent);

        if (speechEvent == SpeechEvent.Start)
        {
            speakingImage.color = new Color(0, 255, 0);
        }

        if (speechEvent == SpeechEvent.End)
        {
            speakingImage.color = new Color(255, 0, 0);
        }

    }

    void OnMicDataAddedEvent(float[] data)
    {
        Debug.Log("Audio data added");
        var filename = DateTime.Now.ToString("yyyy-dd-M--HH-mm-ss") + ".wav";
        record_clip = VADUtil.CreateAudioClip(filename, data,
            audioRecorder.m_AudioSource.clip.channels, (int)audioRecorder.samplingRate);

        var str = "Audio Data added\n duration: " + ((float)data.Length / (int)audioRecorder.samplingRate).ToString() + "s";
        

        if (saveAudioFile)
        {
            var dir = Application.dataPath + "/RecordData/";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            VADUtil.WriteWAVFile(record_clip, dir + filename);
            str += "\n Data Path: " + dir + filename;
        }

        text.text = str;
    }



    public void OnSubmit(string value)
    {
        audioRecorder.SilentTimeInterval = float.Parse(value);
    }

    public void OnSaveToggle(bool value)
    {
        saveAudioFile = value;
    }

    public void OnPlayButtion()
    {
        audioSource.clip = record_clip;
        audioSource.Play();
    }

    public void OnDropdownValueChange()
    {
        audioRecorder.SetAudioRecordDevice(audioDeviceDropdown.options[audioDeviceDropdown.value].text);
    }

    public void OnQuitButtonClick()
    {
        Application.Quit();
    }
}
