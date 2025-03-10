﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information.

//
// Revision history:
//
// ER - December 2013 - Created this file.
//

using System;

#pragma warning disable CA1034 // Nested types should not be visible. (Legacy approach; kept for compat.)
#pragma warning disable CA1720 // Identifier contains type name. (Legacy approach; kept for compat.)

namespace Reaqtor.Remoting.TestingFramework
{
    public static class Constants
    {
        public static class Test
        {
            /// <summary>
            /// The resource identifier for test observer.
            /// </summary>
            public static class TestObserver
            {
                public const string String = "reactor://platform.bing.com/observer/test";
                public static readonly Uri Uri = new(String);
            }

            /// <summary>
            /// The resource identifier for hot playback observable.
            /// </summary>
            public static class HotTimelineObservable
            {
                public const string String = "reactor://platform.bing.com/observable/playback/hot";
                public static readonly Uri Uri = new(String);
            }

            /// <summary>
            /// The resource identifier for cold playback observable.
            /// </summary>
            public static class ColdTimelineObservable
            {
                public const string String = "reactor://platform.bing.com/observable/playback/cold";
                public static readonly Uri Uri = new(String);
            }

            /// <summary>
            /// The resource identifier for the test augmentation observable
            /// </summary>
            public static class StatefulAugmentationObservable
            {
                public const string String = "reactor://platform.bing.com/observable/stateful/augmentation";
                public static readonly Uri Uri = new(String);
            }

            /// <summary>
            /// The resource identifier for assert state transition canary.
            /// </summary>
            public static class AssertStateTransitionCanaryObservable
            {
                public const string String = "reactor://platform.bing.com/observable/assert/transition";
                public static readonly Uri Uri = new(String);
            }
        }
    }
}
