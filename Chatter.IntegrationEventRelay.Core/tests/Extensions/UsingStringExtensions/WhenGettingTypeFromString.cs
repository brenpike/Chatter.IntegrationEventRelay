using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Extensions.UsingStringExtensions
{
    public class WhenGettingTypeFromString : MockContext
    {
        [Fact]
        public void ShouldReturnNullWhenEmptyStringProvided()
        {
            var result = StringExtensions.GetTypeFromString(null);
            Assert.Null(result);
        }

        [Fact]
        public void ShouldReturnNullIfExactTypeForStringIsNotFound()
        {
            var fakeNamespaceString = "I.Am.A.Fake.Namespace";
            var mockType = Context.Common().Type.WithFullName(fakeNamespaceString).Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType).Mock;

            var result = "This.Type.Wont.Be.Found".GetTypeFromString(new[] { mockAssembly });
            Assert.Null(result);
        }

        [Fact]
        public void ShouldReturnNullIfNoTypeEndsWith()
        {
            var fakeNamespaceString = "I.Am.A.Fake.NamespaceSuperset";
            var mockType = Context.Common().Type.WithFullName(fakeNamespaceString).Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType).Mock;

            var result = "I.Am.A.Fake.Namespace".GetTypeFromString(new[] { mockAssembly });
            Assert.Null(result);
        }

        [Fact]
        public void ShouldIgnoreCaseWhenFindingType()
        {
            var fakeNamespaceString = "I.Am.A.Fake.Namespace";
            var mockType = Context.Common().Type.WithFullName(fakeNamespaceString).Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType).Mock;

            var result = fakeNamespaceString.ToUpper().GetTypeFromString(new[] { mockAssembly });
            Assert.Equal(mockType, result);
        }

        [Fact]
        public void ShouldThrowIfSimpleNameMatchesMoreThanOneType()
        {
            var fakeTypeFullNameOne = "Fake.Namespace.One.FakeType";
            var fakeTypeFullNameTwo = "Fake.Namespace.Two.FakeType";
            var mockType = Context.Common().Type.WithFullName(fakeTypeFullNameOne).Mock;
            var mockType2 = Context.Common().Type.WithFullName(fakeTypeFullNameTwo).Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType, mockType2).Mock;

            Assert.Throws<ArgumentException>(() => "FakeType".GetTypeFromString(new[] { mockAssembly }));
        }

        [Fact]
        public void ShouldThrowIfFullNameMatchesMoreThanOneType()
        {
            var fakeTypeFullNameOne = "Fake.Namespace.One.FakeType";
            var mockType = Context.Common().Type.WithFullName(fakeTypeFullNameOne).Mock;
            var mockType2 = Context.Common().Type.WithFullName(fakeTypeFullNameOne).Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType, mockType2).Mock;

            Assert.Throws<ArgumentException>(() => fakeTypeFullNameOne.GetTypeFromString(new[] { mockAssembly }));
        }

        [Fact]
        public void ShouldReturnTypeIfMatchesExactlyOneType()
        {
            var fakeTypeFullNameOne = "Fake.Namespace.One.FakeType";
            var fakeTypeFullNameTwo = "Fake.Namespace.Two.FakeType";
            var mockType = Context.Common().Type.WithFullName(fakeTypeFullNameOne).Mock;
            var mockType2 = Context.Common().Type.WithFullName(fakeTypeFullNameTwo).Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType, mockType2).Mock;

            var result = fakeTypeFullNameOne.GetTypeFromString(new[] { mockAssembly });
            Assert.Equal(mockType, result);
        }

        [Fact]
        public void ShouldReturnNullForPartialNamespaceMatch()
        {
            var fakeTypeFullNameOne = "Fake.Namespace.One.FakeType";
            var fakeTypeFullNameTwo = "Fake.Namespace.Two.FakeType";
            var mockType = Context.Common().Type.WithFullName(fakeTypeFullNameOne).Mock;
            var mockType2 = Context.Common().Type.WithFullName(fakeTypeFullNameTwo).Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType, mockType2).Mock;

            var result = "One.FakeType".GetTypeFromString(new[] { mockAssembly });
            Assert.Null(result);
        }

        [Fact]
        public void ShouldReturnTypeIfSimpleNameMatchesExactlyOneType()
        {
            var mockType = Context.Common().Type.WithFullName("Fake.Namespace.One.FakeType").Mock;
            var mockType2 = Context.Common().Type.WithFullName("Fake.Namespace.Two.OtherFakeType").Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType, mockType2).Mock;

            var result = "FakeType".GetTypeFromString(new[] { mockAssembly });
            Assert.Equal(mockType, result);
        }

        [Fact]
        public void ShouldPreferFullNameOverSimpleName()
        {
            var mockType = Context.Common().Type.WithFullName("FakeType").Mock;
            var mockType2 = Context.Common().Type.WithFullName("Fake.Namespace.FakeType").Mock;
            var mockAssembly = Context.Common().Assembly.WithTypes(mockType, mockType2).Mock;

            var result = "FakeType".GetTypeFromString(new[] { mockAssembly });
            Assert.Equal(mockType, result);
        }
    }
}
