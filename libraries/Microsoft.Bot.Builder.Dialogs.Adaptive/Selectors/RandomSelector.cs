﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Conditions;
using Microsoft.Bot.Expressions;

namespace Microsoft.Bot.Builder.Dialogs.Adaptive.Selectors
{
    /// <summary>
    /// Select a random true triggerHandler implementation of IRuleSelector.
    /// </summary>
    public class RandomSelector : ITriggerSelector
    {
        private List<OnCondition> _conditionals;
        private bool _evaluate;
        private Random _rand;
        private int _seed = -1;

        /// <summary>
        /// Gets or sets optional seed for random number generator.
        /// </summary>
        /// <remarks>If not specified a random seed will be used.</remarks>
        /// <value>
        /// Optional seed for random number generator.
        /// </value>
        public int Seed
        {
            get => _seed;
            set
            {
                _seed = value;
                _rand = new Random(_seed);
            }
        }

        /// <summary>
        /// Gets or sets the expression parser to use.
        /// </summary>
        /// <value>Expression parser.</value>
        [Newtonsoft.Json.JsonIgnore]
        public IExpressionParser Parser { get; set; } = new ExpressionEngine();

        public void Initialize(IEnumerable<OnCondition> conditionals, bool evaluate)
        {
            _conditionals = conditionals.ToList();
            _evaluate = evaluate;
            if (_rand == null)
            {
                _rand = _seed == -1 ? new Random() : new Random(_seed);
            }
        }

        public Task<IReadOnlyList<OnCondition>> Select(SequenceContext context, CancellationToken cancel = default)
        {
            var candidates = _conditionals;
            if (_evaluate)
            {
                candidates = new List<OnCondition>();
                foreach (var conditional in _conditionals)
                {
                    var expression = conditional.GetExpression(Parser);
                    var (value, error) = expression.TryEvaluate(context.GetState());
                    var eval = error == null && (bool)value;
                    if (eval == true)
                    {
                        candidates.Add(conditional);
                    }
                }
            }

            var result = new List<OnCondition>();
            if (candidates.Count > 0)
            {
                var selection = _rand.Next(candidates.Count);
                result.Add(candidates[selection]);
            }

            return Task.FromResult((IReadOnlyList<OnCondition>)result);
        }
    }
}
