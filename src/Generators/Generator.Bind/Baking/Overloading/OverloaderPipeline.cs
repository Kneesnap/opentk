using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bind.Overloaders;
using Bind.XML.Signatures.Functions;
using JetBrains.Annotations;

namespace Bind.Baking.Overloading
{
    /// <summary>
    /// Represents a pipeline of function overloaders that can generate new overloads for injection into a profile.
    /// </summary>
    public class OverloaderPipeline
    {
        /// <summary>
        /// Gets the base pipeline of overloaders.
        /// </summary>
        private readonly IReadOnlyList<IFunctionOverloader> _pipeline;

        /// <summary>
        /// Initializes a new instance of the <see cref="OverloaderPipeline"/> class.
        /// </summary>
        public OverloaderPipeline()
        {
            _pipeline = GetBaselineOverloaders().ToList();
        }

        /// <summary>
        /// Gets the baseline set of function overloaders.
        /// </summary>
        /// <returns>The baseline set.</returns>
        [NotNull, ItemNotNull]
        private IEnumerable<IFunctionOverloader> GetBaselineOverloaders()
        {
            yield return new VoidPointerParameterOverloader();
            yield return new VoidPointerReturnValueOverloader();
            yield return new ReturnTypeConvenienceOverloader();
            yield return new ArrayParameterConvenienceOverloader();
        }

        /// <summary>
        /// Determines whether or not a given function signature has an applicable stage.
        /// </summary>
        /// <param name="signature">The signature.</param>
        /// <returns>True if the function has an applicable stage; otherwise, false.</returns>
        public bool HasApplicableStage(FunctionSignature signature)
        {
            return _pipeline.Any(s => s.IsApplicable(signature));
        }

        /// <summary>
        /// Consumes a set of signatures, passing them through the given pipeline.
        /// </summary>
        /// <param name="signatures">The signatures to process.</param>
        /// <param name="pipeline">A sorted list of generators, acting as the process pipeline.</param>
        /// <returns>The augmented overload list.</returns>
        public IEnumerable<(FunctionSignature, StringBuilder)> ConsumeSignatures
        (
            [NotNull] IEnumerable<FunctionSignature> signatures,
            [CanBeNull] IReadOnlyList<IFunctionOverloader> pipeline = null
        )
        {
            return signatures.SelectMany
            (
                x => (pipeline ?? _pipeline).Where(y => y.IsApplicable(x)).Select(y => y.CreateOverloads(x))
            )
            .SelectMany(x => x);
        }
    }
}
