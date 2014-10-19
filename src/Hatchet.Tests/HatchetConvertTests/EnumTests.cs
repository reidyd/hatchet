﻿using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;

namespace Hatchet.Tests.HatchetConvertTests
{
    [TestFixture]
    public class EnumTests
    {
        public enum TestEnum
        {
            Alpha = 0,
            Bravo = 1,
            Charlie = 2,
            Delta = 3,
            Echo = 4
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
            var result = HatchetConvert.Deserialize<TestEnum>(ref input);

            // Assert
            result.Should().Be(TestEnum.Alpha);
        }

        [Test]
        public void Deserialize_ListOfEnums_ShouldReturnListOfEnums()
        {
            // Arrange
            var input = "[Alpha Bravo charlie DELTA]";

            // Act
            var result = HatchetConvert.Deserialize<List<TestEnum>>(ref input);

            // Assert
            result.Should().HaveCount(4);
            result[0].Should().Be(TestEnum.Alpha);
            result[1].Should().Be(TestEnum.Bravo);
            result[2].Should().Be(TestEnum.Charlie);
            result[3].Should().Be(TestEnum.Delta);
        }

        [Test]
        public void Deserialize_ObjectOfEnums_ShouldSetEnumProperties()
        {
            // Arrange
            var input = "{ Property Alpha Field Delta }";

            // Act
            var result = HatchetConvert.Deserialize<EnumTestClass>(ref input);

            // Assert
            result.Should().NotBeNull();
            result.Property.Should().Be(TestEnum.Alpha);
            result.Field.Should().Be(TestEnum.Delta);
        }
    }
}