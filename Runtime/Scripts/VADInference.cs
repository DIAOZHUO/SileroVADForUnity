using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;



namespace UnityVAD
{


    public enum SpeechEvent
    {
        Start,
        End
    }

    public enum SamplingRate
    {
        sr_8kHz = 8000,
        sr_16kHz = 16000
    }

    public class VADInference
    {

        public bool triggered = false;
        public float threshold = 0.5f;

        public VADUtil.EventHandler<SpeechEvent> SpeechEventHandler;


        private InferenceSession session;
        private readonly SessionOptions options;
        private readonly SamplingRate samplingRate;

        public bool Ready => session != null;

        // silero_vad.onnx v5 input layout:
        //   input: (1, CONTEXT + WIN) = context(64) + chunk(512) = 576
        //   state: (2, 1, 128) float32, fed back each call
        //   sr:    int64 scalar
        // NOTE: feeding bare WIN samples (no context prepended) makes v5 output ~0 silently.
        public const int WIN = 512;        // new samples per step (hop)
        public const int CONTEXT = 64;     // samples prepended from previous step

        int[] srNodeDims = new int[] { 1 };
        int[] stateNodeDims = new int[] { 2, 1, 128 };
        int[] inputNodeDims = new int[] { 1, CONTEXT + WIN };

        float[] stateData = new float[2 * 1 * 128];
        float[] context = new float[CONTEXT];




        public VADInference(SamplingRate sr, float threshold)
        {

            options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = 1
            };

            samplingRate = sr;
            this.threshold = threshold;


        }

        // Build the session from raw model bytes. Required on Android/WebGL where the
        // StreamingAssets model lives inside the compressed package and cannot be opened
        // by file path -- the caller reads the bytes (UnityWebRequest) and passes them here.
        public void LoadModel(byte[] model)
        {
            session = new InferenceSession(model, options);
        }


        private void ResetState()
        {
            stateData = new float[2 * 1 * 128];
            context = new float[CONTEXT];
            triggered = false;
        }

        private float InferenceOnnx(float[] chunk)
        {
            // Build the 576-sample input: context(64) + new samples(512, zero-padded if short).
            var buf = new float[CONTEXT + WIN];
            Array.Copy(context, 0, buf, 0, CONTEXT);
            Array.Copy(chunk, 0, buf, CONTEXT, Math.Min(chunk.Length, WIN));

            var inputTensor = new DenseTensor<float>(new System.Memory<float>(buf), inputNodeDims);
            var srTensor = new DenseTensor<long>(new System.Memory<long>(new long[] { (long)samplingRate }), srNodeDims);
            var stateTensor = new DenseTensor<float>(new System.Memory<float>(stateData), stateNodeDims);


            var inputOnnxValues = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
                NamedOnnxValue.CreateFromTensor("state", stateTensor),
                NamedOnnxValue.CreateFromTensor("sr", srTensor)
            };

            var results = session.Run(inputOnnxValues);
            var scores = results.ElementAt(0).AsTensor<float>().ToArray();

            stateData = results.ElementAt(1).AsTensor<float>().ToArray();

            // carry the last CONTEXT samples of this step as the next step's context
            Array.Copy(buf, WIN, context, 0, CONTEXT);

            return scores[0];
        }

        public void UpdateSpeechData(float[] input)
        {
            if (session == null) return;   // model not loaded yet (async load on Android/WebGL)
            var output = InferenceOnnx(input);
            // speech start
            if ((output >= threshold) && (triggered == false))
            {
                triggered = true;
                // the start sample is the previous input
                SpeechEventHandler?.Invoke(SpeechEvent.Start);
            }
            // speech end
            if ((output < (threshold - 0.15)) && (triggered == true))
            {
                triggered = false;
                ResetState();
                SpeechEventHandler?.Invoke(SpeechEvent.End);
            }
        }


    }

}
