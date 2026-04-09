using System.Collections.Generic;

public class MovingAverage
{
    private Queue<float> samples = new Queue<float>();
    private int windowSize;
    private float sum = 0;

    public MovingAverage(int size)
    {
        windowSize = size;
    }

    public float AddSample(float value)
    {
        samples.Enqueue(value);
        sum += value;

        if (samples.Count > windowSize)
        {
            sum -= samples.Dequeue();
        }

        return sum / samples.Count;
    }
}

