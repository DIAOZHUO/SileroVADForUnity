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


        private readonly InferenceSession session;
        private readonly SamplingRate samplingRate;

        int[] srNodeDims = new int[] { 1 };
        int[] hcNodeDims = new int[] { 2, 1, 64 };
        int inputSampleCount;

        float[] hData = new float[128];
        float[] cData = new float[128];




        public VADInference(string modelPath, SamplingRate sr, float threshold, int sampleCount)
        {

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = 1
            };

            session = new InferenceSession(modelPath, options);
            inputSampleCount = sampleCount;
            samplingRate = sr;
            this.threshold = threshold;


        }


        private void ResetState()
        {
            hData = new float[128];
            cData = new float[128];
            triggered = false;
        }

        private float InferenceOnnx(float[] input)
        {
            int channel = input.Length / inputSampleCount;
            

            var inputTensor = new DenseTensor<float>(new System.Memory<float>(input), new int[] { channel, input.Length });
            var srTensor = new DenseTensor<long>(new System.Memory<long>(new long[] { (long)samplingRate }), srNodeDims);
            var hTensor = new DenseTensor<float>(new System.Memory<float>(hData), hcNodeDims);
            var cTensor = new DenseTensor<float>(new System.Memory<float>(cData), hcNodeDims);


            var inputOnnxValues = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
                NamedOnnxValue.CreateFromTensor("sr", srTensor),
                NamedOnnxValue.CreateFromTensor("h", hTensor),
                NamedOnnxValue.CreateFromTensor("c", cTensor)
            };

            var results = session.Run(inputOnnxValues);
            var scores = results.ElementAt(0).AsTensor<float>().ToArray();

            hData = results.ElementAt(1).AsTensor<float>().ToArray();
            cData = results.ElementAt(2).AsTensor<float>().ToArray();

            return scores[0];
        }

        public void UpdateSpeechData(float[] input)
        {
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