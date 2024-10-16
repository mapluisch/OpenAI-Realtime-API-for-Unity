using Unity.VisualScripting;
using UnityEngine;

public static class AudioProcessingUtils
{
    public static float energyLast;
    /// <summary>
    /// converts pcm16 audio data to float array
    /// </summary>
    public static float[] ConvertPCM16ToFloat(byte[] pcmAudioData)
    {
        int length = pcmAudioData.Length / 2;
        float[] floatData = new float[length];
        for (int i = 0; i < length; i++)
        {
            short sample = System.BitConverter.ToInt16(pcmAudioData, i * 2);
            floatData[i] = sample / 32768f;
        }
        return floatData;
    }

    /// <summary>
    /// converts float audio data to base64-encoded pcm16 string
    /// </summary>
    public static string ConvertFloatToPCM16AndBase64(float[] audioData)
    {
        byte[] pcm16Audio = new byte[audioData.Length * 2];
        for (int i = 0; i < audioData.Length; i++)
        {
            short value = (short)(Mathf.Clamp(audioData[i], -1f, 1f) * short.MaxValue);
            pcm16Audio[i * 2] = (byte)(value & 0xFF);
            pcm16Audio[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return System.Convert.ToBase64String(pcm16Audio);
    }

    /// <summary>
    /// performs fft on audio data (only used for visualization atm)
    /// </summary>
    public static void FFT(float[] data, float[] spectrum)
    {
        int n = data.Length;
        int m = (int)Mathf.Log(n, 2);
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            if (i < j)
            {
                float temp = data[i];
                data[i] = data[j];
                data[j] = temp;
            }
            int k = n >> 1;
            while (k >= 1 && k <= j)
            {
                j -= k;
                k >>= 1;
            }
            j += k;
        }
        for (int l = 1; l <= m; l++)
        {
            int le = 1 << l;
            int le2 = le >> 1;
            float ur = 1.0f;
            float ui = 0.0f;
            float sr = Mathf.Cos(Mathf.PI / le2);
            float si = -Mathf.Sin(Mathf.PI / le2);
            for (int j1 = 0; j1 < le2; j1++)
            {
                for (int i = j1; i < n; i += le)
                {
                    int ip = i + le2;
                    float tr = data[ip] * ur - 0 * ui;
                    float ti = data[ip] * ui + 0 * ur;
                    data[ip] = data[i] - tr;
                    data[i] += tr;
                }
                float temp = ur;
                ur = temp * sr - ui * si;
                ui = temp * si + ui * sr;
            }
        }
        for (int i = 0; i < n / 2; i++)
        {
            spectrum[i] = Mathf.Sqrt(data[i] * data[i] + data[n - i - 1] * data[n - i - 1]);
        }
    }

    /// <summary>
    /// performs simple voice activity detection
    /// 
    /// credit @Macoron - source: https://raw.githubusercontent.com/Macoron/whisper.unity/275406258aca21fe7753cf0724a65f06fd464eea/Packages/com.whisper.unity/Runtime/Utils/AudioUtils.cs
    /// </summary>
    public static bool SimpleVad(float[] data, int sampleRate, float lastSec, float vadThd, float freqThd)
    {
        var nSamples = data.Length;
        var nSamplesLast = (int)(sampleRate * lastSec);

        if (nSamplesLast >= nSamples) return false;

        if (freqThd > 0.0f) HighPassFilter(data, freqThd, sampleRate);

        var energyAll = 0.0f;
        var energyLast = 0.0f;

        for (var i = 0; i < nSamples; i++)
        {
            energyAll += Mathf.Abs(data[i]);
            if (i >= nSamples - nSamplesLast) energyLast += Mathf.Abs(data[i]);
        }

        energyAll /= nSamples;
        energyLast /= nSamplesLast;
        AudioProcessingUtils.energyLast = energyLast;

        return energyLast > vadThd * energyAll;
    }

    /// <summary>
    /// applies high-pass filter to audio data
    ///
    /// credit @Macoron - source: https://raw.githubusercontent.com/Macoron/whisper.unity/275406258aca21fe7753cf0724a65f06fd464eea/Packages/com.whisper.unity/Runtime/Utils/AudioUtils.cs
    /// </summary>
    public static void HighPassFilter(float[] data, float cutoff, int sampleRate)
    {
        if (data.Length == 0)
            return;
        var rc = 1.0f / (2.0f * Mathf.PI * cutoff);
        var dt = 1.0f / sampleRate;
        var alpha = dt / (rc + dt);
        var y = data[0];
        for (var i = 1; i < data.Length; i++)
        {
            y = alpha * (y + data[i] - data[i - 1]);
            data[i] = y;
        }
    }
}
