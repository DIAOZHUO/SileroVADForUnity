# SileroVADForUnity
 Silero VAD is a free library to do Voice activity detection task. 
 You can find the origin and the detail at https://github.com/snakers4/silero-vad.
 This project runs in unity.

 The demo/unitypackage for Windows and Mac can be found in Release.

### Feature
- Voice activity detection from microphone
- Extract voice data and save as wav file


### Usage


Put the "AudioRecorder" prefab in your scene and reference it.
```c#
    AudioRecorder audioRecorder = AudioRecorder.Instance;

    // Detect the event when the speech start and end
    audioRecorder.MIC_SpeechEventHandler += OnMicSpeechEvent;
    void OnMicSpeechEvent(SpeechEvent speechEvent)
    {
        Debug.Log(speechEvent);

        if (speechEvent == SpeechEvent.Start)
        {
            //do something
        }

        if (speechEvent == SpeechEvent.End)
        {
            //do something
        }
    }
    
    // Detect the event when the new speech sample added
    audioRecorder.MIC_DataAddedEventHandler += OnMicDataAddedEvent;
    void OnMicDataAddedEvent(float[] data)
    {
        Debug.Log("Audio data added");
    }
```