// ============================================================
// Builders.cs  →  SDMS.Tests/Helpers/
// Shared factory methods for constructing domain objects in tests.
// Every test file imports this — keeps test data construction DRY.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using SDMS.Domain.Models;
using SDMS.Domain.Scanner;
using SDMS.Domain.Scoring;

namespace SDMS.Tests.Helpers;

internal static class Builders
{
    // ── FileNode ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal file FileNode. All optional fields have safe defaults.
    /// </summary>
    internal static FileNode File(
        string name          = "test.txt",
        string? fullPath     = null,
        long   sizeBytes     = 1024,
        string mimeType      = "document",
        FileAttributes attrs = FileAttributes.Normal,
        DateTime? accessed   = null,
        DateTime? modified   = null,
        string? hash         = null,
        int    depth         = 1,
        int    siblings      = 0)
    {
        fullPath ??= Path.Combine("C:\\Root", name);
        var now = DateTime.UtcNow;
        var node = new FileNode
        {
            Name         = name,
            FullPath     = fullPath,
            RelativePath = name,
            Depth        = depth,
            IsDirectory  = false,
            SizeBytes    = sizeBytes,
            CreatedAt    = now.AddDays(-30),
            ModifiedAt   = modified ?? now.AddDays(-7),
            AccessedAt   = accessed ?? now.AddDays(-1),
            Attributes   = attrs,
            MimeType     = mimeType,
            ParentChain  = Array.Empty<string>(),
        };
        node.Hash        = hash;
        node.NumSiblings = siblings;
        return node;
    }

    /// <summary>
    /// Builds a directory FileNode with optional children.
    /// </summary>
    internal static FileNode Dir(
        string name         = "Root",
        string? fullPath    = null,
        int    depth        = 0,
        List<FileNode>? children = null)
    {
        fullPath ??= Path.Combine("C:\\", name);
        var node = new FileNode
        {
            Name         = name,
            FullPath     = fullPath,
            RelativePath = name,
            Depth        = depth,
            IsDirectory  = true,
            SizeBytes    = 0,
            CreatedAt    = DateTime.UtcNow.AddDays(-60),
            ModifiedAt   = DateTime.UtcNow.AddDays(-1),
            AccessedAt   = DateTime.UtcNow,
            Attributes   = FileAttributes.Directory,
            MimeType     = "directory",
            ParentChain  = Array.Empty<string>(),
        };
        if (children != null)
            node.Children.AddRange(children);
        return node;
    }

    // ── FileTree ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a FileTree rooted at a Dir node with the given children.
    /// Also populates BasicInfo.ExtensionCounts from the children.
    /// </summary>
    internal static FileTree Tree(FileNode root, FolderAnalysisMetrics? metrics = null)
    {
        metrics ??= MetricsFromRoot(root);
        return new FileTree
        {
            Root         = root,
            ScannedAt    = DateTime.UtcNow,
            ScanRootPath = root.FullPath,
            BasicInfo    = metrics,
        };
    }

    /// <summary>
    /// Computes a FolderAnalysisMetrics by walking the FileNode tree.
    /// Good enough for test purposes — mirrors what TreeWalker does.
    /// </summary>
    internal static FolderAnalysisMetrics MetricsFromRoot(FileNode root)
    {
        var m = new FolderAnalysisMetrics();
        Walk(root, m);
        return m;
    }

    private static void Walk(FileNode node, FolderAnalysisMetrics m)
    {
        if (!node.IsDirectory)
        {
            m.TotalFiles++;
            m.TotalSizeBytes += node.SizeBytes;
            var ext = node.Extension;
            m.ExtensionCounts[ext] = m.ExtensionCounts.GetValueOrDefault(ext) + 1;
            m.ExtensionSizes[ext]  = m.ExtensionSizes.GetValueOrDefault(ext) + node.SizeBytes;
            var cat = Mimeclassifier.Classify(ext);
            m.CategoryCounts[cat] = m.CategoryCounts.GetValueOrDefault(cat) + 1;
        }
        else
        {
            m.TotalDirectories++;
        }
        foreach (var child in node.Children)
            Walk(child, m);
    }

    // ── AnalysisReport ────────────────────────────────────────────────────────

    internal static AnalysisReport Report(FileTree? tree = null)
    {
        tree ??= Tree(Dir("Root"));
        return new AnalysisReport
        {
            SourceTree           = tree,
            AnalyzedAt           = DateTime.UtcNow,
            FileTypeDistribution = new Dictionary<string, int>(tree.BasicInfo.ExtensionCounts),
            FileTypeSizeMap      = new Dictionary<string, long>(tree.BasicInfo.ExtensionSizes),
            IgnoredFiles         = new List<FileNode>(),
            SystemFiles          = new List<FileNode>(),
            RequiredLabels       = new List<string>(),
        };
    }

    // ── PlannedOperation ─────────────────────────────────────────────────────

    internal static PlannedOperation MoveOp(
        string src  = "C:\\Root\\file.txt",
        string dest = "C:\\Root\\Docs\\file.txt",
        OpStatus status = OpStatus.Pending,
        double confidence = 0.9,
        float importance  = 5.0f)
    {
        return new PlannedOperation
        {
            Id              = Guid.NewGuid(),
            Type            = OpType.Move,
            SourcePath      = src,
            DestinationPath = dest,
            Reason          = "Test move",
            Confidence      = confidence,
            Importance      = importance,
            Status          = status,
            IsUserAdded     = false,
        };
    }

    internal static PlannedOperation DeleteOp(
        string src = "C:\\Root\\junk.tmp",
        OpStatus status = OpStatus.Pending)
    {
        return new PlannedOperation
        {
            Id         = Guid.NewGuid(),
            Type       = OpType.Delete,
            SourcePath = src,
            Reason     = "Test delete",
            Confidence = 0.8,
            Importance = 3.0f,
            Status     = status,
        };
    }

    internal static PlannedOperation NewFolderOp(string dest = "C:\\Root\\Docs")
    {
        return new PlannedOperation
        {
            Id              = Guid.NewGuid(),
            Type            = OpType.NewFolder,
            DestinationPath = dest,
            Reason          = "Create folder",
            Confidence      = 1.0,
            Importance      = 10.0f,
            Status          = OpStatus.Confirmed,
        };
    }

    // ── ProposedPlan ─────────────────────────────────────────────────────────

    internal static ProposedPlan ProposedPlan(
        List<PlannedOperation>? ops    = null,
        AnalysisReport?         report = null)
    {
        report ??= Report();
        return new ProposedPlan
        {
            Operations   = ops ?? new List<PlannedOperation>(),
            SourceReport = report,
            GeneratedAt  = DateTime.UtcNow,
        };
    }

    // ── ScanOptions ───────────────────────────────────────────────────────────

    internal static ScanOptions DefaultOptions() => new ScanOptions();

    internal static ScanOptions PermissiveOptions() => new ScanOptions
    {
        IncludeHidden      = true,
        IncludeSystemFiles = true,
        FollowSymlinks     = false,
        ExcludedDirectoryNames = new List<string>(),
        ExcludedExtensions     = new List<string>(),
    };

    // ── ScoringWeights ────────────────────────────────────────────────────────

    internal static ScoringWeights EqualWeights() => new ScoringWeights
    {
        RecencyWeight          = 1.0,
        ModifiedRecencyWeight  = 1.0,
        FileTypePriorityWeight = 1.0,
        LargeSizePenaltyWeight = 1.0,
        DuplicatePenaltyWeight = 1.0,
        SystemFilePenaltyWeight = 1.0,
    };
}
