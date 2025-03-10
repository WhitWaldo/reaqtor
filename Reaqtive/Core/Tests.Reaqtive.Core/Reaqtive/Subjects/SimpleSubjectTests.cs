﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Reaqtive;

namespace Test.Reaqtive
{
    [TestClass]
    public class SimpleSubjectTests
    {
        [TestMethod]
        public void SimpleSubject_Checks()
        {
            var s = new SimpleSubject<int>();

            var o1 = s.CreateObserver();
            var o2 = s.CreateObserver();

            Assert.AreSame(o1, o2);

            Assert.ThrowsException<ArgumentNullException>(() => s.Subscribe(null));
        }

        [TestMethod]
        public void SimpleSubject_Basics_OnNext()
        {
            var s = new SimpleSubject<int>();

            var o1 = s.CreateObserver();

            o1.OnNext(42);

            var s1 = new List<int>();
            var d1 = s.Subscribe(s1.Add);

            o1.OnNext(43);

            var s2 = new List<int>();
            var d2 = s.Subscribe(s2.Add);

            o1.OnNext(44);

            var s3 = new List<int>();
            var d3 = s.Subscribe(s3.Add);

            o1.OnNext(45);

            d1.Dispose();

            o1.OnNext(46);

            d3.Dispose();

            o1.OnNext(47);

            var s4 = new List<int>();
            var d4 = s.Subscribe(s4.Add);

            o1.OnNext(48);

            d2.Dispose();
            d4.Dispose();

            o1.OnNext(49);

            var s5 = new List<int>();
            var d5 = s.Subscribe(s5.Add);

            o1.OnNext(50);

            Assert.IsTrue(new[] { 43, 44, 45 }.SequenceEqual(s1));
            Assert.IsTrue(new[] { 44, 45, 46, 47, 48 }.SequenceEqual(s2));
            Assert.IsTrue(new[] { 45, 46 }.SequenceEqual(s3));
            Assert.IsTrue(new[] { 48 }.SequenceEqual(s4));
            Assert.IsTrue(new[] { 50 }.SequenceEqual(s5));
        }

        [TestMethod]
        public void SimpleSubject_Basics_DoubleDisposeIsFine()
        {
            var s = new SimpleSubject<int>();

            var o1 = s.CreateObserver();

            var d = s.Subscribe(_ => { Assert.Fail(); });

            d.Dispose();

            o1.OnNext(42);

            d.Dispose();

            o1.OnNext(42);
        }

        [TestMethod]
        public void SimpleSubject_DisposedBehavior()
        {
            var s = new SimpleSubject<int>();

            var d1 = s.Subscribe();

            for (int i = 0; i < 2; i++)
            {
                s.Dispose();

                Assert.ThrowsException<ObjectDisposedException>(() => s.OnNext(42));
                Assert.ThrowsException<ObjectDisposedException>(() => s.OnError(new Exception()));
                Assert.ThrowsException<ObjectDisposedException>(s.OnCompleted);

                d1.Dispose();

                Assert.ThrowsException<ObjectDisposedException>(() => s.Subscribe());
            }
        }
    }
}
