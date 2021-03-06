﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Caching;
using NewLife.Security;
using NewLife.Threading;
using Xunit;

namespace XUnitTest
{
    public class QueueTests
    {
        private FullRedis _redis;

        public QueueTests()
        {
            _redis = new FullRedis("127.0.0.1:6379", null, 2);
        }

        [Fact]
        public void Queue_Normal()
        {
            var key = "qkey_normal";

            // 删除已有
            _redis.Remove(key);
            var q = _redis.GetQueue<String>(key);
            _redis.SetExpire(key, TimeSpan.FromMinutes(60));

            var queue = q as RedisQueue<String>;
            Assert.NotNull(queue);

            // 取出个数
            var count = q.Count;
            Assert.True(q.IsEmpty);
            Assert.Equal(0, count);

            // 添加
            var vs = new[] { "1234", "abcd", "新生命团队", "ABEF" };
            q.Add(vs);

            // 对比个数
            var count2 = q.Count;
            Assert.False(q.IsEmpty);
            Assert.Equal(count + vs.Length, count2);

            // 取出来
            var vs2 = q.Take(2).ToArray();
            Assert.Equal(2, vs2.Length);
            Assert.Equal("1234", vs2[0]);
            Assert.Equal("abcd", vs2[1]);

            // 管道批量获取
            var q2 = q as RedisQueue<String>;
            q2.MinPipeline = 4;
            var vs3 = q.Take(5).ToArray();
            Assert.Equal(2, vs3.Length);
            Assert.Equal("新生命团队", vs3[0]);
            Assert.Equal("ABEF", vs3[1]);

            // 对比个数
            var count3 = q.Count;
            Assert.True(q.IsEmpty);
            Assert.Equal(count, count3);
        }

        [Fact]
        public void Queue_Strict()
        {
            var key = "qkey_strict";

            // 删除已有
            _redis.Remove(key);
            var q = _redis.GetQueue<String>(key);
            _redis.SetExpire(key, TimeSpan.FromMinutes(60));

            var queue = q as RedisQueue<String>;
            Assert.NotNull(queue);

            //_redis.Remove(queue.AckKey);
            queue.Strict = true;
            //Assert.Equal(key + "_ack", queue.AckKey);
            Assert.StartsWith(key + ":Ack:", queue.AckKey);

            // 取出个数
            var count = q.Count;
            Assert.True(q.IsEmpty);
            Assert.Equal(0, count);

            // 添加
            var vs = new[] { "1234", "abcd", "新生命团队", "ABEF" };
            q.Add(vs);

            // 取出来
            var vs2 = q.Take(3).ToArray();
            Assert.Equal(3, vs2.Length);
            Assert.Equal("1234", vs2[0]);
            Assert.Equal("abcd", vs2[1]);
            Assert.Equal("新生命团队", vs2[2]);

            Assert.Equal(1, q.Count);

            // 确认队列
            var q2 = _redis.GetQueue<String>(queue.AckKey) as RedisQueue<String>;
            Assert.Equal(vs2.Length, q2.Count);

            // 确认两个
            var rs = queue.Acknowledge(vs2.Take(2));
            Assert.Equal(2, rs);
            Assert.Equal(1, q2.Count);

            // 捞出来Ack最后一个
            var vs3 = queue.TakeAck(3).ToArray();
            Assert.Equal(0, q2.Count);
            Assert.Single(vs3);
            Assert.Equal("新生命团队", vs3[0]);

            // 读取队列最后一个
            var vs4 = q.Take(44).ToArray();
            Assert.Single(vs4);

            q2.Take(1).ToArray();
        }

        [Fact]
        public void Queue_NotEnough()
        {
            var key = "qkey_not_enough";

            // 删除已有
            _redis.Remove(key);
            var q = _redis.GetQueue<String>(key);
            _redis.SetExpire(key, TimeSpan.FromMinutes(60));

            var queue = q as RedisQueue<String>;
            Assert.NotNull(queue);

            // 取出个数
            var count = q.Count;
            Assert.True(q.IsEmpty);
            Assert.Equal(0, count);

            // 添加
            var vs = new[] { "1234", "abcd" };
            q.Add(vs);

            // 取出来
            var vs2 = q.Take(3).ToArray();
            Assert.Equal(2, vs2.Length);
            Assert.Equal("1234", vs2[0]);
            Assert.Equal("abcd", vs2[1]);

            // 再取，这个时候已经没有元素
            var vs4 = q.Take(3).ToArray();
            Assert.Empty(vs4);

            // 管道批量获取
            var vs3 = q.Take(5).ToArray();
            Assert.Empty(vs3);

            // 对比个数
            var count3 = q.Count;
            Assert.True(q.IsEmpty);
            Assert.Equal(count, count3);
        }

        /// <summary>AckKey独一无二，一百万个key测试</summary>
        [Fact]
        public void UniqueAckKey()
        {
            var key = "qkey_unique";

            var hash = new HashSet<String>();

            for (var i = 0; i < 1_000_000; i++)
            {
                var q = _redis.GetQueue<String>(key) as RedisQueue<String>;

                //Assert.DoesNotContain(q.AckKey, hash);
                Assert.False(hash.Contains(q.AckKey));

                hash.Add(q.AckKey);
            }
        }

        [Fact]
        public void Queue_Benchmark()
        {
            var key = "qkey_benchmark";

            var q = _redis.GetQueue<String>(key);
            for (var i = 0; i < 1_000; i++)
            {
                var list = new List<String>();
                for (var j = 0; j < 100; j++)
                {
                    list.Add(Rand.NextString(32));
                }
                q.Add(list);
            }

            Assert.Equal(1_000 * 100, q.Count);

            var count = 0;
            while (true)
            {
                var n = Rand.Next(1, 100);
                var list = q.Take(n).ToList();
                if (list.Count == 0) break;

                count += list.Count;
            }

            Assert.Equal(1_000 * 100, count);
        }

        [Fact]
        public void Queue_Benchmark_Mutilate()
        {
            var key = "qkey_benchmark_mutilate";
            _redis.Remove(key);

            var q = _redis.GetQueue<String>(key);
            for (var i = 0; i < 1_000; i++)
            {
                var list = new List<String>();
                for (var j = 0; j < 100; j++)
                {
                    list.Add(Rand.NextString(32));
                }
                q.Add(list);
            }

            Assert.Equal(1_000 * 100, q.Count);

            var count = 0;
            var ths = new List<Task>();
            for (var i = 0; i < 16; i++)
            {
                ths.Add(Task.Run(() =>
                {
                    while (true)
                    {
                        var n = Rand.Next(1, 100);
                        var list = q.Take(n).ToList();
                        if (list.Count == 0) break;

                        Interlocked.Add(ref count, list.Count);
                    }
                }));
            }

            Task.WaitAll(ths.ToArray());

            Assert.Equal(1_000 * 100, count);
        }

        [Fact]
        public void Queue_Benchmark_Strict()
        {
            var key = "qkey_benchmark_strict";

            var q = _redis.GetQueue<String>(key);
            var queue = q as RedisQueue<String>;
            queue.Strict = true;

            for (var i = 0; i < 1_000; i++)
            {
                var list = new List<String>();
                for (var j = 0; j < 100; j++)
                {
                    list.Add(Rand.NextString(32));
                }
                q.Add(list);
            }

            Assert.Equal(1_000 * 100, q.Count);

            var count = 0;
            while (true)
            {
                var n = Rand.Next(1, 100);
                var list = q.Take(n).ToList();
                if (list.Count == 0) break;

                var n2 = queue.Acknowledge(list);
                Assert.Equal(list.Count, n2);

                count += list.Count;
            }

            Assert.Equal(1_000 * 100, count);
        }

        [Fact]
        public void RetryDeadAck()
        {
            var key = "qkey_RetryDeadAck";

            _redis.Remove(key);
            var q = _redis.GetQueue<String>(key);
            var queue = q as RedisQueue<String>;
            queue.Strict = true;
            queue.RetryInterval = 2;

            // 清空
            queue.TakeAllAck().ToArray();

            // 生产几个消息，消费但不确认
            var list = new List<String>();
            for (var i = 0; i < 5; i++)
            {
                list.Add(Rand.NextString(32));
            }
            q.Add(list);

            var list2 = q.Take(10).ToList();
            Assert.Equal(list.Count, list2.Count);

            // 确认队列里面有几个
            var q2 = _redis.GetList<String>(queue.AckKey);
            Assert.Equal(list.Count, q2.Count);

            // 马上消费，消费不到
            var vs3 = q.Take(100).ToArray();
            Assert.Empty(vs3);

            // 等一定时间再消费
            Thread.Sleep(queue.RetryInterval * 1000 + 10);

            // 再次消费，应该有了
            var vs4 = q.Take(100).ToArray();
            Assert.Equal(list.Count, vs4.Length);

            // 确认队列里面的私信重新进入主队列，消费时再次进入确认队列
            Assert.Equal(vs4.Length, q2.Count);

            // 全部确认
            queue.Acknowledge(vs4);

            // 确认队列应该空了
            Assert.Equal(0, q2.Count);
        }
    }
}