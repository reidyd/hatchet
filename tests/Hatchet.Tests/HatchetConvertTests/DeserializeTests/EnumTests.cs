﻿using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;

namespace Hatchet.Tests.HatchetConvertTests.DeserializeTests
{
    [TestFixture]
    public class EnumTests
    {
        [Flags]
        public enum TestEnum
        {
            Alpha = 1,
            Bravo = 2,
            Charlie = 4,
            Delta = 8,
            Echo = 16
        }

        public class EnumTestClass
        {
            public TestEnum Property;
            public TestEnum Field;
        }

        [Test]
        public void Deserialize_SingleEnum_ShouldReturnAnEnum()
        {
            // Arrange
            var input = "Alpha";

            // Act
            var result = HatchetConvert.Deserialize<TestEnum>(input);

            // Assert
            result.Should().Be(TestEnum.Alpha);
        }

        [Test]
        public void Deserialize_ListOfEnums_ShouldReturnListOfEnums()
        {
            // Arrange
            var input = "[Alpha Bravo charlie DELTA]";

            // Act
            var result = HatchetConvert.Deserialize<List<TestEnum>>(input);

            // Assert
            result.Should().ContainInOrder(TestEnum.Alpha, TestEnum.Bravo, TestEnum.Charlie, TestEnum.Delta);
        }

        [Test]
        public void Deserialize_ObjectOfEnums_ShouldSetEnumProperties()
        {
            // Arrange
            var input = "{ Property Alpha Field Delta }";

            // Act
            var result = HatchetConvert.Deserialize<EnumTestClass>(input);

            // Assert
            result.Should().NotBeNull();
            result.Property.Should().Be(TestEnum.Alpha);
            result.Field.Should().Be(TestEnum.Delta);
        }

        [Test]
        public void Deserialize_EnumFlags_CorrectFlagsAreSet()
        {
            // Arrange
            var input = "[ALpha Bravo]";

            // Act
            var result = HatchetConvert.Deserialize<TestEnum>(input);

            // Assert
            result.HasFlag(TestEnum.Alpha).Should().BeTrue();
            result.HasFlag(TestEnum.Bravo).Should().BeTrue();

            result.HasFlag(TestEnum.Charlie).Should().BeFalse();
            result.HasFlag(TestEnum.Delta).Should().BeFalse();
            result.HasFlag(TestEnum.Echo).Should().BeFalse();
        }

        [Test]
        public void Deserialize_EmptyEnumFlags_NoFlagsAreSet()
        {
            // Arrange
            var input = "[]";

            // Act
            var result = HatchetConvert.Deserialize<TestEnum>(input);

            // Assert
            result.HasFlag(TestEnum.Alpha).Should().BeFalse();
            result.HasFlag(TestEnum.Bravo).Should().BeFalse();
            result.HasFlag(TestEnum.Charlie).Should().BeFalse();
            result.HasFlag(TestEnum.Delta).Should().BeFalse();
            result.HasFlag(TestEnum.Echo).Should().BeFalse();
        }
    }
}