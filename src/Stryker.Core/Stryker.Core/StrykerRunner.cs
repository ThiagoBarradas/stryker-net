﻿using Buildalyzer;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Stryker.Core.Exceptions;
using Stryker.Core.Initialisation;
using Stryker.Core.Logging;
using Stryker.Core.Mutants;
using Stryker.Core.MutationTest;
using Stryker.Core.Options;
using Stryker.Core.ProjectComponents;
using Stryker.Core.Reporters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;

namespace Stryker.Core
{
    public interface IStrykerRunner
    {
        StrykerRunResult RunMutationTest(StrykerOptions options, IEnumerable<LogMessage> initialLogMessages = null);
    }

    public class StrykerRunner : IStrykerRunner
    {
        private IReporter _reporters;
        private readonly IProjectOrchestrator _projectOrchestrator;
        private IEnumerable<IMutationTestProcess> _mutationTestProcesses;
        private readonly IFileSystem _fileSystem;
        private ILogger _logger;

        public StrykerRunner(IProjectOrchestrator projectOrchestrator = null, IFileSystem fileSystem = null)
        {
            _projectOrchestrator = projectOrchestrator ?? new ProjectOrchestrator();
            _mutationTestProcesses = new List<IMutationTestProcess>();
            _fileSystem = fileSystem ?? new FileSystem();
        }

        /// <summary>
        /// Starts a mutation test run
        /// </summary>
        /// <exception cref="StrykerInputException">For managed exceptions</exception>
        /// <param name="options">The user options</param>
        /// <param name="initialLogMessages">
        /// Allows to pass log messages that occured before the mutation test.
        /// The messages will be written to the logger after it was configured.
        /// </param>
        public StrykerRunResult RunMutationTest(StrykerOptions options, IEnumerable<LogMessage> initialLogMessages = null)
        {
            // start stopwatch
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            CreateOutputDirWithGitignore(options);

            // setup logging
            ApplicationLogging.ConfigureLogger(options.LogOptions, initialLogMessages);
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<StrykerRunner>();

            _logger.LogDebug("Stryker started with options: {0}",
                JsonConvert.SerializeObject(options, new StringEnumConverter()));

            _reporters = ReporterFactory.Create(options);

            try
            {
                _mutationTestProcesses = _projectOrchestrator.MutateProjects(options, _reporters).ToList();

                var rootComponent = new FolderComposite();

                foreach(var project in _mutationTestProcesses)
                {
                    rootComponent.Add(project.Input.ProjectInfo.ProjectContents);
                }

                _logger.LogInformation("{0} mutants ready for test", rootComponent.TotalMutants.Count());

                if (options.Optimizations.HasFlag(OptimizationFlags.SkipUncoveredMutants) || options.Optimizations.HasFlag(OptimizationFlags.CoverageBasedTest))
                {
                    _logger.LogInformation($"Capture mutant coverage using '{options.OptimizationMode}' mode.");

                    foreach (var project in _mutationTestProcesses)
                    {
                        project.GetCoverage();
                    }
                }
                _reporters.OnMutantsCreated(rootComponent);

                var allMutants = rootComponent.Mutants.ToList();
                var mutantsNotRun = allMutants.Where(x => x.ResultStatus == MutantStatus.NotRun).ToList();

                if (!mutantsNotRun.Any())
                {
                    if (allMutants.Any(x => x.ResultStatus == MutantStatus.Ignored))
                    {
                        _logger.LogWarning("It looks like all mutants with tests were excluded. Try a re-run with less exclusion!");
                    }
                    if (allMutants.Any(x => x.ResultStatus == MutantStatus.NoCoverage))
                    {
                        _logger.LogWarning("It looks like all non-excluded mutants are not covered by a test. Go add some tests!");
                    }
                    if (!allMutants.Any())
                    {
                        _logger.LogWarning("It\'s a mutant-free world, nothing to test.");
                        return new StrykerRunResult(options, double.NaN);
                    }
                }

                var mutantsToTest = mutantsNotRun.Where(x => x.ResultStatus != MutantStatus.Ignored && x.ResultStatus != MutantStatus.NoCoverage);
                _reporters.OnStartMutantTestRun(mutantsNotRun);

                foreach (var project in _mutationTestProcesses)
                {
                    // test mutations
                    project.Test(project.Input.ProjectInfo.ProjectContents.Mutants.Where(x => x.ResultStatus == MutantStatus.NotRun).ToList());
                }

                _reporters.OnAllMutantsTested(rootComponent);

                return new StrykerRunResult(options, rootComponent.GetMutationScore());
            }
            catch (Exception ex) when (!(ex is StrykerInputException))
            {
                _logger.LogError(ex, "An error occurred during the mutation test run ");
                throw;
            }
            finally
            {
                // log duration
                stopwatch.Stop();
                _logger.LogInformation("Time Elapsed {0}", stopwatch.Elapsed);
            }
        }

        private void CreateOutputDirWithGitignore(StrykerOptions options)
        {
            _fileSystem.Directory.CreateDirectory(options.OutputPath);
            _fileSystem.File.Create(Path.Combine(options.OutputPath, ".gitignore")).Close();
            using (var file = _fileSystem.File.CreateText(Path.Combine(options.OutputPath, ".gitignore")))
            {
                file.WriteLine("*");
            }
        }
    }
}
