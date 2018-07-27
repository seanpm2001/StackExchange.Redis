﻿using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess;
using BenchmarkDotNet.Validators;
using StackExchange.Redis;

namespace BasicTest
{
    internal static class Program
    {
        private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
    }
    internal class CustomConfig : ManualConfig
    {
        public CustomConfig()
        {
            Job Get(Job j) => j
                .With(new GcMode { Force = true })
                .With(InProcessToolchain.Instance)
                ;

            Add(new MemoryDiagnoser());
            Add(StatisticColumn.OperationsPerSecond);
            Add(JitOptimizationsValidator.FailOnError);
            Add(Get(Job.Clr));
            //Add(Get(Job.Core));
        }
    }
    /// <summary>
    /// The tests
    /// </summary>
    [Config(typeof(CustomConfig))]
    public class RedisBenchmarks : IDisposable
    {
        SocketManager mgr;
        ConnectionMultiplexer connection;
        IDatabase db;

        /// <summary>
        /// Create
        /// </summary>
        public RedisBenchmarks()
        {
            var options = ConfigurationOptions.Parse("127.0.0.1:6379");
            connection = ConnectionMultiplexer.Connect(options);
            db = connection.GetDatabase(3);

            db.KeyDelete(GeoKey, CommandFlags.FireAndForget);
            db.GeoAdd(GeoKey, 13.361389, 38.115556, "Palermo ");
            db.GeoAdd(GeoKey, 15.087269, 37.502669, "Catania");
        }
        static readonly RedisKey GeoKey = "GeoTest", IncrByKey = "counter";
        void IDisposable.Dispose()
        {
            mgr?.Dispose();
            connection?.Dispose();
            mgr = null;
            db = null;
            connection = null;
        }

        private const int COUNT = 500;

        /// <summary>
        /// Run INCRBY lots of times
        /// </summary>
#if TEST_BASELINE
        [Benchmark(Description = "INCRBY:v1/s", OperationsPerInvoke = COUNT)]
#else
        [Benchmark(Description = "INCRBY:v2/s", OperationsPerInvoke = COUNT)]
#endif
        public int ExecuteIncrBy()
        {
            var rand = new Random(12345);
            
            db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
            int expected = 0;
            for (int i = 0; i < COUNT; i++)
            {
                int x = rand.Next(50);
                expected += x;
                db.StringIncrement(IncrByKey, x, CommandFlags.FireAndForget);
            }
            int actual = (int)db.StringGet(IncrByKey);
            if (actual != expected) throw new InvalidOperationException($"expected: {expected}, actual: {actual}");
            return actual;
        }

        /// <summary>
        /// Run INCRBY lots of times
        /// </summary>
#if TEST_BASELINE
        [Benchmark(Description = "INCRBY:v1/a", OperationsPerInvoke = COUNT)]
#else
        [Benchmark(Description = "INCRBY:v2/a", OperationsPerInvoke = COUNT)]
#endif
        public async Task<int> ExecuteIncrByAsync()
        {
            var rand = new Random(12345);

            db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
            int expected = 0;
            for (int i = 0; i < COUNT; i++)
            {
                int x = rand.Next(50);
                expected += x;
                await db.StringIncrementAsync(IncrByKey, x, CommandFlags.FireAndForget);
            }
            int actual = (int)await db.StringGetAsync(IncrByKey);
            if (actual != expected) throw new InvalidOperationException($"expected: {expected}, actual: {actual}");
            return actual;
        }

        /// <summary>
        /// Run GEORADIUS lots of times
        /// </summary>
#if TEST_BASELINE
        [Benchmark(Description = "GEORADIUS:v1/s", OperationsPerInvoke = COUNT)]
#else
        [Benchmark(Description = "GEORADIUS:v2/s", OperationsPerInvoke = COUNT)]
#endif
        public int ExecuteGeoRadius()
        {
            int total = 0;
            for (int i = 0; i < COUNT; i++)
            {
                var results = db.GeoRadius(GeoKey, 15, 37, 200, GeoUnit.Kilometers,
                    options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash);
                total += results.Length;
            }
            return total;
        }

        /// <summary>
        /// Run GEORADIUS lots of times
        /// </summary>
#if TEST_BASELINE
        [Benchmark(Description = "GEORADIUS:v1/a", OperationsPerInvoke = COUNT)]
#else
        [Benchmark(Description = "GEORADIUS:v2/a", OperationsPerInvoke = COUNT)]
#endif
        public async Task<int> ExecuteGeoRadiusAsync()
        {
            int total = 0;
            for (int i = 0; i < COUNT; i++)
            {
                var results = await db.GeoRadiusAsync(GeoKey, 15, 37, 200, GeoUnit.Kilometers,
                    options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash);
                total += results.Length;
            }
            return total;
        }
    }
#pragma warning disable CS1591
    [Config(typeof(CustomConfig))]
    public class Issue898 : IDisposable
    {
        private readonly ConnectionMultiplexer mux;
        private readonly IDatabase db;

        public void Dispose() => mux?.Dispose();
        public Issue898()
        {
            mux = ConnectionMultiplexer.Connect("localhost:6379");
            db = mux.GetDatabase();
        }


        const int max = 100000;
        [Benchmark(OperationsPerInvoke = max)]
        public void Load()
        {
            for (int i = 0; i < max; ++i)
            {
                db.StringSet(i.ToString(), i);
            }
        }
        [Benchmark(OperationsPerInvoke = max)]
        public async Task LoadAsync()
        {
            for (int i = 0; i < max; ++i)
            {
                await db.StringSetAsync(i.ToString(), i);
            }
        }
        [Benchmark(OperationsPerInvoke = max)]
        public void Sample()
        {            
            var rnd = new Random();

            for (int i = 0; i < max; ++i)
            {
                var r = rnd.Next(0, max - 1);

                var rv = db.StringGet(r.ToString());
                if (rv != r)
                {
                    throw new Exception($"Unexpected {rv}, expected {r}");
                }
            }
        }

        [Benchmark(OperationsPerInvoke = max)]
        public async Task SampleAsync()
        {
            var rnd = new Random();

            for (int i = 0; i < max; ++i)
            {
                var r = rnd.Next(0, max - 1);

                var rv = await db.StringGetAsync(r.ToString());
                if (rv != r)
                {
                    throw new Exception($"Unexpected {rv}, expected {r}");
                }
            }
        }
    }
}
