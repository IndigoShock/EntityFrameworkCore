﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming
namespace Microsoft.EntityFrameworkCore
{
    public class ModelSourceTest
    {
        private readonly IConventionSetBuilder _nullConventionSetBuilder
            = new NullConventionSetBuilder();

        [ConditionalFact]
        public void OnModelCreating_is_only_called_once()
        {
            const int threadCount = 5;

            var models = new IModel[threadCount];

            Parallel.For(
                0, threadCount,
                i =>
                {
                    using (var context = new SlowContext())
                    {
                        models[i] = context.Model;
                    }
                });

            Assert.NotNull(models[0]);

            foreach (var model in models)
            {
                Assert.Same(models[0], model);
            }

            Assert.Equal(1, SlowContext.CallCount);
        }

        private class SlowContext : DbContext
        {
            public static int CallCount { get; private set; }

            protected internal override void OnModelCreating(ModelBuilder modelBuilder)
            {
                CallCount++;
                Thread.Sleep(200);
            }

            protected internal override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder
                    .UseInternalServiceProvider(InMemoryFixture.DefaultServiceProvider)
                    .UseInMemoryDatabase(nameof(SlowContext));
        }

        [ConditionalFact]
        public void Adds_all_entities_based_on_all_distinct_entity_types_found()
        {
            var setFinder = new FakeSetFinder();

            var model = CreateDefaultModelSource(setFinder)
                .GetModel(
                    InMemoryTestHelpers.Instance.CreateContext(),
                    new RuntimeConventionSetBuilder(
                        new ProviderConventionSetBuilder(
                            InMemoryTestHelpers.Instance.CreateContextServices()
                                .GetRequiredService<ProviderConventionSetBuilderDependencies>()
                                .With(setFinder)), new List<IConventionSetPlugin>()));

            Assert.Equal(
                new[] { typeof(SetA).DisplayName(), typeof(SetB).DisplayName() },
                model.GetEntityTypes().Select(e => e.Name).ToArray());
        }

        private class FakeModelValidator : IModelValidator
        {
            public void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
            {
            }
        }

        private class FakeSetFinder : IDbSetFinder
        {
            public IReadOnlyList<DbSetProperty> FindSets(Type contextType)
                => new[]
                {
                    new DbSetProperty("One", typeof(SetA), setter: null),
                    new DbSetProperty("Two", typeof(SetB), setter: null),
                    new DbSetProperty("Three", typeof(SetA), setter: null)
                };
        }

        private class JustAClass
        {
            public DbSet<Random> One { get; set; }
            protected DbSet<object> Two { get; set; }
            private DbSet<string> Three { get; set; }
            private DbSet<string> Four { get; set; }
        }

        private class SetA
        {
            public int Id { get; set; }
        }

        private class SetB
        {
            public int Id { get; set; }
        }

        [ConditionalFact]
        public void Caches_model_by_context_type()
        {
            var modelSource = CreateDefaultModelSource(new DbSetFinder());

            var model1 = modelSource.GetModel(new Context1(), _nullConventionSetBuilder);
            var model2 = modelSource.GetModel(new Context2(), _nullConventionSetBuilder);

            Assert.NotSame(model1, model2);
            Assert.Same(model1, modelSource.GetModel(new Context1(), _nullConventionSetBuilder));
            Assert.Same(model2, modelSource.GetModel(new Context2(), _nullConventionSetBuilder));
        }

        [ConditionalFact]
        public void Stores_model_version_information_as_annotation_on_model()
        {
            var modelSource = CreateDefaultModelSource(new DbSetFinder());

            var model = modelSource.GetModel(new Context1(), _nullConventionSetBuilder);
            var packageVersion = typeof(Context1).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(m => m.Key == "PackageVersion").Value;

            Assert.StartsWith(packageVersion, model.GetProductVersion(), StringComparison.OrdinalIgnoreCase);
        }

        private class Context1 : DbContext
        {
        }

        private class Context2 : DbContext
        {
        }

        private IModelSource CreateDefaultModelSource(IDbSetFinder setFinder)
            => new ConcreteModelSource(setFinder);

        private class ConcreteModelSource : ModelSource
        {
            public ConcreteModelSource(IDbSetFinder setFinder)
                : base(
                    new ModelSourceDependencies(
                        new ModelCustomizer(new ModelCustomizerDependencies(setFinder)),
                        InMemoryTestHelpers.Instance.CreateContextServices().GetRequiredService<IModelCacheKeyFactory>(),
                        new MemoryCache(new MemoryCacheOptions() { SizeLimit = 200 })))
            {
            }
        }

        private class NullConventionSetBuilder : IConventionSetBuilder
        {
            public ConventionSet CreateConventionSet() => new ConventionSet();
        }
    }
}
