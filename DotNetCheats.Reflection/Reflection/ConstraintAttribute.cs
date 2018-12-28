using System;

namespace Cheats.Reflection
{
    /// <summary>
    /// Indicates that generic parameter is constrained with a concept.
    /// </summary>
    [AttributeUsage(AttributeTargets.GenericParameter, AllowMultiple = false, Inherited = true)]
    public sealed class ConstraintAttribute: Attribute
    {
        /// <summary>
        /// Initializes a new attribute and specify concept type.
        /// </summary>
        /// <param name="conceptType">Concept type.</param>
        public ConstraintAttribute(Type conceptType) => Concept = conceptType;

        /// <summary>
        /// Gets type of concept.
        /// </summary>
        public Type Concept { get; }
    }
}