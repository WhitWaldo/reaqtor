// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT License.
// See the LICENSE file in the project root for more information.

using System;

using Reaqtive;

namespace Reaqtor.Reactive
{
    internal sealed class MultiSubjectSubscriptionProxy<TInput, TOutput> : UnaryOperator<Uri, TOutput>
    {
#pragma warning disable CA2213
        private readonly SingleAssignmentSubscription _subscription = new();
#pragma warning restore CA2213

        public MultiSubjectSubscriptionProxy(Uri uri, IObserver<TOutput> observer)
            : base(uri, observer)
        {
        }

        protected override ISubscription OnSubscribe()
        {
            return _subscription;
        }

        public override void SetContext(IOperatorContext context)
        {
            base.SetContext(context);

            var sub = context.ExecutionEnvironment.GetSubject<TInput, TOutput>(Params).Subscribe(Output);

            SubscriptionInitializeVisitor.Subscribe(sub);

            _subscription.Subscription = sub;
        }

        /// <summary>
        /// Defines behavior immediately after we `Dispose` of the operator. This method is
        /// usually called at the very end of the `Dispose` method.
        /// </summary>
        protected override void OnDispose()
        {
            _subscription?.Dispose();

            base.OnDispose();
        }
    }
}
