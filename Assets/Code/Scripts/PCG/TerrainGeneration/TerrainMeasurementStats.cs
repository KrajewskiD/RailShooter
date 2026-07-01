using System.Collections.Generic;

public struct TerrainMeasurementStats
{
    public int count;
    public double min;
    public double max;
    public double mean;
    public double median;

    public static TerrainMeasurementStats From(List<double> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            return default;
        }

        List<double> validSamples = new List<double>(samples.Count);

        double sum = 0.0;
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        for (int i = 0; i < samples.Count; i++)
        {
            double value = samples[i];
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                continue;
            }

            validSamples.Add(value);
            sum += value;
            if (value < min) min = value;
            if (value > max) max = value;
        }

        int validCount = validSamples.Count;
        if (validCount == 0)
        {
            return default;
        }

        validSamples.Sort();
        double median = validSamples[validCount / 2];
        if (validCount % 2 == 0)
        {
            median = (validSamples[(validCount / 2) - 1] + validSamples[validCount / 2]) * 0.5;
        }

        return new TerrainMeasurementStats
        {
            count = validCount,
            min = min,
            max = max,
            mean = sum / validCount,
            median = median
        };
    }
}
