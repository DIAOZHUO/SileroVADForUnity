using System;
using System.Collections.Generic;

namespace UnityVAD
{
    /// <summary>A speech segment cut by VADSegmenter.</summary>
    public class VADSegment
    {
        /// Mono audio at the stream's sample rate.
        public float[] Audio;
        /// Absolute stream index of Audio[0].
        public long StartSample;
        /// Speech probability of every 512-sample chunk of Audio (ChunkProbs[i] covers
        /// Audio[i*512 .. (i+1)*512)), from the streaming VAD whose state is warm. Use these
        /// instead of re-running a fresh VAD on the segment, which would miss its first words.
        public float[] ChunkProbs;
    }

    /// <summary>
    /// Cuts a continuous 16 kHz stream into speech segments with Silero VAD.
    /// Feed it consecutive 512-sample chunks (VADInference.WIN); it raises SegmentReady with the
    /// audio from the first speech start (minus FixTrimLength chunks of pre-roll) to the last
    /// speech end, once SilentTimeInterval seconds of silence follow it.
    ///
    /// Plain C# (no UnityEngine), so the exact same logic runs on the microphone (AudioRecorder)
    /// and on recorded files in tests. Silence is counted in audio time, not frame time.
    /// </summary>
    public class VADSegmenter
    {
        public readonly VADInference Vad;

        /// Seconds of silence after the last speech end that close a segment.
        public float SilentTimeInterval = 1.5f;
        /// 512-sample chunks of audio kept before the detected speech start.
        public int FixTrimLength = 1;
        /// Close a segment after this many seconds even if speech continues (0 = no limit).
        /// The next segment starts right after, so long monologues are reported while they go on.
        public float MaxSegmentSeconds = 0f;

        public event Action<VADSegment> SegmentReady;

        /// Samples fed so far (absolute stream position of the next chunk).
        public long SamplesProcessed { get; private set; }
        public bool InSegment => open;

        const int W = VADInference.WIN;
        readonly int sampleRate;
        readonly Queue<(float[] chunk, float prob)> preRoll = new Queue<(float[], float)>();
        readonly List<float> segment = new List<float>(16000 * 8);
        readonly List<float> probs = new List<float>(256);
        long segmentStart;
        int endLength;            // segment length at the last speech end
        bool open;
        bool afterEnd;            // speech ended and has not resumed yet
        int silentSamples;

        public VADSegmenter(VADInference vad, int sampleRate = 16000)
        {
            Vad = vad;
            this.sampleRate = sampleRate;
        }

        /// <summary>Process one chunk of exactly VADInference.WIN samples.</summary>
        public void Process(float[] chunk)
        {
            if (chunk.Length != W) throw new ArgumentException($"chunk must be {W} samples");
            long cur = SamplesProcessed;
            bool before = Vad.triggered;
            Vad.UpdateSpeechData(chunk);
            float prob = Vad.LastProbability;
            bool started = !before && Vad.triggered;
            bool ended = before && !Vad.triggered;

            if (started)
            {
                if (!open)
                {
                    open = true;
                    segment.Clear();
                    probs.Clear();
                    segmentStart = cur - (long)preRoll.Count * W;
                    foreach (var (c, p) in preRoll) { segment.AddRange(c); probs.Add(p); }
                }
                afterEnd = false;          // speech resumed: stop the silence timer
                silentSamples = 0;
            }

            if (open)
            {
                if (ended)
                {
                    endLength = (int)(cur - segmentStart);     // up to (not incl.) the chunk that fell below
                    afterEnd = true;
                    silentSamples = 0;
                }
                segment.AddRange(chunk);
                probs.Add(prob);

                if (afterEnd)
                {
                    silentSamples += W;
                    if (silentSamples >= SilentTimeInterval * sampleRate) Close(endLength);
                }
                else if (MaxSegmentSeconds > 0f && segment.Count >= MaxSegmentSeconds * sampleRate)
                {
                    // still speaking: emit what we have and continue in a new segment
                    Emit(segment.Count);
                    segment.Clear();
                    probs.Clear();
                    segmentStart = cur + W;
                }
            }

            preRoll.Enqueue(((float[])chunk.Clone(), prob));
            while (preRoll.Count > Math.Max(0, FixTrimLength)) preRoll.Dequeue();
            SamplesProcessed = cur + W;
        }

        /// <summary>Emit the open segment (if any), e.g. before switching devices or stopping.</summary>
        public void Flush()
        {
            if (open) Close(afterEnd ? endLength : segment.Count);
        }

        /// <summary>Drop all state, including the VAD model state, before an unrelated stream.</summary>
        public void Reset()
        {
            open = afterEnd = false;
            segment.Clear();
            probs.Clear();
            preRoll.Clear();
            silentSamples = 0;
            Vad.ResetState();
        }

        void Close(int length)
        {
            Emit(length);
            open = afterEnd = false;
            segment.Clear();
            probs.Clear();
            silentSamples = 0;
        }

        void Emit(int length)
        {
            if (length <= 3 * W) return;               // ignore blips (same rule as before)
            length = Math.Min(length, segment.Count);
            SegmentReady?.Invoke(new VADSegment
            {
                Audio = segment.GetRange(0, length).ToArray(),
                StartSample = segmentStart,
                ChunkProbs = probs.GetRange(0, Math.Min(probs.Count, (length + W - 1) / W)).ToArray(),
            });
        }
    }
}
