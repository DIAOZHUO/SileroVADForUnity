# SileroVADForUnity
 Silero VAD is a free library to do Voice activity detection task. 
 You can find the origin and the detail at https://github.com/snakers4/silero-vad.
 This project runs in unity.

 The demo/unitypackage for Windows and Mac can be found in Release.

### Feature
- Voice activity detection from microphone
- Extract voice data and save as wav file


### Installation

Please install via package manager.

1. In the Unity Editor, open the Package Manager by navigating to Window > Package Manager.
2. Add (+) button in the top-left corner of the Package Manager toolbar.
3. Select Add package from git URL.
4. Paste https://github.com/DIAOZHUO/SileroVADForUnity.git
5. (quick getting start) In the Package Window, Samples > import "VAD Test Demo" Scene.

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

### Inspector settings (AudioRecorder)

| Setting | Prefab default | Description |
|---|---|---|
| `DetectThreshold` | 0.5 | VAD speech probability that starts speech (speech ends below this − 0.15). Higher = only clear speech. |
| `SilentTimeInterval` | 1.5 | Seconds of silence after speech that close a segment. Use 0.2–0.3 for conversation, so turns are not merged. |
| `FixTrimLength` | 2 | 512-sample chunks (32 ms each) kept before the detected speech start. |
| `MaxSegmentSeconds` | 0 | Emit a segment every N seconds while speech continues (0 = only when speech ends). |
| `MicBufferSeconds` | 10 | Length of the looping microphone buffer; the recording is never restarted. |

### Advanced

- `MIC_SegmentAddedEventHandler` delivers the same segments as `MIC_DataAddedEventHandler` as a `VADSegment`,
  which also carries the segment's start position in the stream and the VAD probability of every 512-sample chunk.
- `VADSegmenter` is the segmentation logic behind `AudioRecorder`. Feed it 512-sample chunks of any 16 kHz stream
  (e.g. an audio file in a test) to get exactly the same segments as from the microphone.
