namespace Solar;

public class SolarOptions
{
    public const string Section = "Solar";

    public string InverterHost { get; set; } = "192.168.17.22";
    public int InverterPort { get; set; } = 4196;
    public string OpenHabBaseUrl { get; set; } = "http://192.168.17.220:8888/rest/items";
    public int PollIntervalSeconds { get; set; } = 5;
}
