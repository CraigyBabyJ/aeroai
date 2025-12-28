using System;
using System.Collections.Generic;
using AeroAI.AtcSession;
using Xunit;

namespace AeroAI.Tests;

public class ReadbackValidatorTemplateTests
{
    [Fact]
    public void MissingInfoConfig_ExposesSlotPriorities()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);
        Assert.Contains("atis", packs!.MissingInfo.SlotPriorities);
        Assert.Equal("atis", packs.MissingInfo.SlotPriorities[0]);
    }

    [Fact]
    public void MissingInfoPrompt_RendersAtisTemplate()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var renderer = new AtcTemplateRenderer(packs!);
        var data = new Dictionary<string, string>
        {
            ["callsign"] = "TEST123",
            ["destination_name"] = "Juneau International",
            ["atis_letter"] = "Alpha"
        };

        var prompt = renderer.RenderMissingInfoPrompt("atis", "clearance", "delivery", data);

        Assert.NotNull(prompt);
        Assert.Contains("TEST123", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("information", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadbackTail_RendersRoleSpecificTemplate()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var renderer = new AtcTemplateRenderer(packs!);
        var data = new Dictionary<string, string>
        {
            ["callsign"] = "TEST123"
        };

        var tail = renderer.RenderReadbackAcknowledgementTail("clearance", "delivery", data);

        Assert.Equal("Call ready for push and start.", tail);
    }
}
