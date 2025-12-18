namespace Flux.Domain;

public record MonitorOffset(double X, double Y)
{
    public static MonitorOffset Zero => new(0, 0);
}
