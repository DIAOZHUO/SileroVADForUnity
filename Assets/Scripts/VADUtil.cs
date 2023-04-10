using System;
using System.IO;
using System.Text;
using UnityEngine;
using System.Collections.Generic;
using System.Threading;

namespace UnityVAD {


    public static class VADUtil
    {
        public delegate void EventHandler<T>(T args);



        public static AudioClip CreateAudioClip(string name, float[] data, int channel, int sampling_rate)
        {
            AudioClip audioClip = AudioClip.Create(name, data.Length, channel, sampling_rate, false);
            audioClip.SetData(data, 0);
            return audioClip;
        }



        public static void WriteWAVFile(AudioClip clip, string filePath)
        {
            if (!filePath.ToLower().EndsWith(".wav"))
            {
                filePath += ".wav";
            }






            //Create the file.
            using (Stream fs = File.Create(filePath))
            {
                float[] clipData = new float[clip.samples * clip.channels];

                clip.GetData(clipData, 0);
                short[] intData = new short[clipData.Length];
                byte[] bytesData = new byte[clipData.Length * 2];

                int convertionFactor = 32767;

                for (int i = 0; i < clipData.Length; i++)
                {
                    intData[i] = (short)(clipData[i] * convertionFactor);
                    byte[] byteArr = new byte[2];
                    byteArr = BitConverter.GetBytes(intData[i]);
                    byteArr.CopyTo(bytesData, i * 2);
                }

                fs.Write(bytesData, 0, bytesData.Length);

                var hz = clip.frequency;
                var channels = clip.channels;
                var samples = clip.samples;

                fs.Seek(0, SeekOrigin.Begin);

                byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
                fs.Write(riff, 0, 4);

                byte[] chunkSize = BitConverter.GetBytes(fs.Length - 8);
                fs.Write(chunkSize, 0, 4);

                byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
                fs.Write(wave, 0, 4);

                byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
                fs.Write(fmt, 0, 4);

                byte[] subChunk1 = BitConverter.GetBytes(16);
                fs.Write(subChunk1, 0, 4);

                ushort two = 2;
                ushort one = 1;

                byte[] audioFormat = BitConverter.GetBytes(one);
                fs.Write(audioFormat, 0, 2);

                byte[] numChannels = BitConverter.GetBytes(channels);
                fs.Write(numChannels, 0, 2);

                byte[] sampleRate = BitConverter.GetBytes(hz);
                fs.Write(sampleRate, 0, 4);

                byte[] byteRate = BitConverter.GetBytes(hz * channels * 2); // sampleRate * bytesPerSample*number of channels, here 44100*2*2
                fs.Write(byteRate, 0, 4);

                ushort blockAlign = (ushort)(channels * 2);
                fs.Write(BitConverter.GetBytes(blockAlign), 0, 2);

                ushort bps = 16;
                byte[] bitsPerSample = BitConverter.GetBytes(bps);
                fs.Write(bitsPerSample, 0, 2);

                byte[] datastring = System.Text.Encoding.UTF8.GetBytes("data");
                fs.Write(datastring, 0, 4);

                byte[] subChunk2 = BitConverter.GetBytes(samples * channels * 2);
                fs.Write(subChunk2, 0, 4);
            }
        }
    }
}