using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Solar;

public class SolarWorker(
    InverterClient inverter,
    OpenHabClient openHab,
    IOptions<SolarOptions> options,
    ILogger<SolarWorker> logger) : BackgroundService
{
    private readonly SolarOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("START");

        // Initial protocol query
        await inverter.SendCommandAsync("QPI", ct: ct);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), ct);
            await PollStatusAsync(ct);

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), ct);
            await PollEnergyAsync(ct);

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), ct);
        }
    }

    private async Task PollStatusAsync(CancellationToken ct)
    {
        bool success = false;

        var line = await inverter.SendCommandAsync("QPIGS 1", ct: ct);
        if (line != null)
        {
            var v = InverterClient.ParseResponse(line);
            if (v != null)
            {
                success = true;
                logger.LogInformation(
                    "STATUS Grid:{GridV}V/{GridP}W/{GridF}Hz Output:{OutV}V/{OutP}W/{OutF}Hz/{OutL}% " +
                    "Battery:{BatV}V/{BatC}% PV1:{Pv1V}V/{Pv1A}A/{Pv1P}W Temp:{Temp}C",
                    v[0], v[4], v[1], v[2], v[5], v[3], v[6], v[8], v[10], v[13], v[12], v[19], v[11]);
                var tasks = new List<Task<bool>>
                {
                    openHab.PostStateAsync("s_grid_voltage", v[0], ct),
                    openHab.PostStateAsync("s_grid_power", v[4], ct),
                    openHab.PostStateAsync("s_grid_frequency", v[1], ct),
                    openHab.PostStateAsync("s_output_voltage", v[2], ct),
                    openHab.PostStateAsync("s_output_power", v[5], ct),
                    openHab.PostStateAsync("s_output_frequency", v[3], ct),
                    openHab.PostStateAsync("s_output_load", v[6], ct),
                    openHab.PostStateAsync("s_battery_voltage", v[8], ct),
                    openHab.PostStateAsync("s_battery_capacity", v[10], ct),
                    openHab.PostStateAsync("s_pv1_current", v[12], ct),
                    openHab.PostStateAsync("s_pv1_voltage", v[13], ct),
                    openHab.PostStateAsync("s_pv1_power", v[19], ct),
                    openHab.PostStateAsync("s_system_temperature", v[11], ct),
                };
                await Task.WhenAll(tasks);
            }
        }

        line = await inverter.SendCommandAsync("QPIGS2 1", ct: ct);
        if (line != null)
        {
            var v = InverterClient.ParseResponse(line);
            if (v != null)
            {
                success = true;
                logger.LogInformation("STATUS PV2:{Pv2V}V/{Pv2A}A/{Pv2P}W", v[1], v[0], v[2]);
                var tasks = new List<Task<bool>>
                {
                    openHab.PostStateAsync("s_pv2_current", v[0], ct),
                    openHab.PostStateAsync("s_pv2_voltage", v[1], ct),
                    openHab.PostStateAsync("s_pv2_power", v[2], ct),
                };
                await Task.WhenAll(tasks);
            }
        }

        if (!success)
            logger.LogWarning("Status poll failed");
    }

    private async Task PollEnergyAsync(CancellationToken ct)
    {
        bool success = false;
        double energyTotal = 0, energyYear = 0, energyMonth = 0, energyDay = 0;
        var now = DateTime.Now;

        var line = await inverter.SendCommandAsync("QET 1", ct: ct);
        if (line != null)
        {
            var v = InverterClient.ParseResponse(line);
            if (v != null)
            {
                await openHab.PostStateAsync("s_energy_total", v[0], ct);
                success = true;
                energyTotal = v[0];
            }
        }

        line = await inverter.SendCommandAsync($"QEY{now.Year:D4} 1", ct: ct);
        if (line != null)
        {
            var v = InverterClient.ParseResponse(line);
            if (v != null)
                energyYear = v[0];
        }

        line = await inverter.SendCommandAsync($"QEM{now.Year:D4}{now.Month:D2} 1", ct: ct);
        if (line != null)
        {
            var v = InverterClient.ParseResponse(line);
            if (v != null)
                energyMonth = v[0];
        }

        line = await inverter.SendCommandAsync($"QED{now.Year:D4}{now.Month:D2}{now.Day:D2} 1", ct: ct);
        if (line != null)
        {
            var v = InverterClient.ParseResponse(line);
            if (v != null)
            {
                await openHab.PostStateAsync("s_energy_day", v[0], ct);
                success = true;
                energyDay = v[0];
            }
        }

        if (success)
            logger.LogInformation("ENERGY Total:{Total}Wh Year:{Year}Wh Month:{Month}Wh Day:{Day}Wh",
                energyTotal, energyYear, energyMonth, energyDay);
        else
            logger.LogWarning("Energy poll failed");
    }
}
