using VarVault.Domain.Dependencies;

namespace VarVault.Common.Tests;

/// <summary>Unit coverage for the VaM-log ref extractor — the robust replacement for the old scraping regex. (QoL.)</summary>
public class VamLogParserTests
{
    private static string Ref(DependencyRef d) =>
        d.VersionKind == VersionSpecKind.Latest ? $"{d.Creator}.{d.Package}.latest" : $"{d.Creator}.{d.Package}.{d.ExactVersion}";

    [Fact]
    public void Extracts_missing_addon_package_lines()
    {
        var log = "!> Missing addon package MacGruber.PostMagic.4\n!> Missing addon package Fallen.Masako.7";
        var refs = VamLogParser.ExtractRefs(log).Select(Ref).ToList();
        Assert.Contains("MacGruber.PostMagic.4", refs);
        Assert.Contains("Fallen.Masako.7", refs);
    }

    [Fact]
    public void Extracts_colon_and_path_embedded_refs()
    {
        // The old regex needed a trailing colon; this must catch a ref buried in a path too.
        var log = "AcidBubbles.Timeline.312:/Custom/Scripts/Timeline/foo.cs not found";
        var refs = VamLogParser.ExtractRefs(log).Select(Ref).ToList();
        Assert.Contains("AcidBubbles.Timeline.312", refs);
    }

    [Fact]
    public void Extracts_dot_latest_which_the_old_regex_missed()
    {
        var refs = VamLogParser.ExtractRefs("Missing addon package Creator.Pack.latest").ToList();
        Assert.Single(refs);
        Assert.Equal(VersionSpecKind.Latest, refs[0].VersionKind);
        Assert.Equal("Creator.Pack.latest", Ref(refs[0]));
    }

    [Fact]
    public void Strips_trailing_var_extension()
    {
        var refs = VamLogParser.ExtractRefs("could not load Creator.Pack.2.var").Select(Ref).ToList();
        Assert.Contains("Creator.Pack.2", refs);
    }

    [Fact]
    public void Keeps_different_versions_as_distinct_and_dedups_repeats()
    {
        var log = "Creator.Pack.1 Creator.Pack.1 Creator.Pack.2";
        var refs = VamLogParser.ExtractRefs(log).Select(Ref).ToList();
        Assert.Equal(2, refs.Count);
        Assert.Contains("Creator.Pack.1", refs);
        Assert.Contains("Creator.Pack.2", refs);
    }

    [Fact]
    public void Handles_cjk_creator_names()
    {
        var refs = VamLogParser.ExtractRefs("Missing addon package 衣裙.鞋子.3").Select(Ref).ToList();
        Assert.Contains("衣裙.鞋子.3", refs);
    }

    [Fact]
    public void Real_log_takes_missing_ref_not_the_depender_tail()
    {
        // Real VaM output: the depender is glued to "package" (no space) — a naive scan would mis-read it.
        var log = """
            !> Missing addon package VL_13.Harness_AV.latest that packageVL_13.Bodysuit_RW.1 depends on
            !> Missing addon package VL_13.Swim_V2.latest that packageVL_13.Bodysuit_RW.1 depends on
            """;
        var refs = VamLogParser.ExtractRefs(log).Select(Ref).ToList();
        Assert.Equal(["VL_13.Harness_AV.latest", "VL_13.Swim_V2.latest"], refs);
        Assert.DoesNotContain(refs, r => r.Contains("Bodysuit_RW"));   // the depender is not a missing item
    }

    [Fact]
    public void Ignores_non_identity_noise()
    {
        // File paths, sentences, and two-part or non-numeric tokens are not refs.
        var log = "Loading scene Custom/Scripts/thing.cs at 12.34 fps; version 1.2.beta done.";
        Assert.Empty(VamLogParser.ExtractRefs(log));
    }

    [Fact]
    public void Empty_or_null_in_empty_out()
    {
        Assert.Empty(VamLogParser.ExtractRefs(null));
        Assert.Empty(VamLogParser.ExtractRefs("   "));
    }

    [Fact]
    public void Extracts_creator_with_spaces_and_plus_in_package()
    {
        var log = """
            !> Missing addon package Kamiyama Prod.Preset_Asuna_22y_V2.latest that packageMibkev.Princess_Destruction.1 depends on
            !> Plugin file Blazedust.ToySerialController+VAMLaunch.12:/Custom/Scripts/Blazedust/ToySerialController+VAMLaunch/ADD_ME.cslist does not exist
            """;
        var refs = VamLogParser.ExtractRefs(log).Select(Ref).ToList();
        Assert.Contains("Kamiyama Prod.Preset_Asuna_22y_V2.latest", refs);
        Assert.Contains("Blazedust.ToySerialController+VAMLaunch.12", refs);
        Assert.DoesNotContain(refs, r => r.Contains("Princess_Destruction"));
    }

    [Fact]
    public void Extracts_plugin_file_lines()
    {
        var log = "!> Plugin file prestigitis.DesktopClothGrab.1:/Custom/Scripts/prestigitis/prestigitis_DesktopClothGrab.cs does not exist";
        var refs = VamLogParser.ExtractRefs(log).Select(Ref).ToList();
        Assert.Contains("prestigitis.DesktopClothGrab.1", refs);
    }
}
