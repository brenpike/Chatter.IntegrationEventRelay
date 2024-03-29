﻿using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;

public class LoggerCreator<T> : MockCreator<ILogger<T>>
{
    private readonly Mock<ILogger<T>> _loggerMock;
    public List<(LogLevel level, string message)> LoggedMessages { get; } = new List<(LogLevel level, string message)>();

    public LoggerCreator(IMockContext newContext, ILogger<T> creation = null)
        : base(newContext, creation)
    {
        _loggerMock = new Mock<ILogger<T>>();
        _loggerMock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback(new InvocationAction(invocation =>
            {
                var logLevel = (LogLevel)invocation.Arguments[0];
                var eventId = (EventId)invocation.Arguments[1];
                var state = invocation.Arguments[2];
                var exception = (Exception)invocation.Arguments[3];
                var formatter = invocation.Arguments[4];

                var invokeMethod = formatter.GetType().GetMethod("Invoke");
                var logMessage = (string)invokeMethod?.Invoke(formatter, new[] { state, exception });

                LoggedMessages.Add((logLevel, logMessage));
            }));
        Mock = _loggerMock.Object;
    }

    public LoggerCreator<T> VerifyWasCalled(LogLevel level, string expectedMessage = null, Times times = default)
    {
        Func<object, Type, bool> state = (v, t) => expectedMessage == null || v.ToString().CompareTo(expectedMessage) == 0;

        _loggerMock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == level),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => state(v, t)),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)), times);

        return this;
    }

    public LoggerCreator<T> WithLogDebugThatThrows() => WithLogOperationThatThrows<Exception>(LogLevel.Debug);
    public LoggerCreator<T> WithLogErrorThatThrows() => WithLogOperationThatThrows<Exception>(LogLevel.Error);
    public LoggerCreator<T> WithLogWarningThatThrows() => WithLogOperationThatThrows<Exception>(LogLevel.Warning);
    public LoggerCreator<T> WithLogTraceThatThrows() => WithLogOperationThatThrows<Exception>(LogLevel.Trace);
    public LoggerCreator<T> WithLogInfoThatThrows() => WithLogOperationThatThrows<Exception>(LogLevel.Information);
    public LoggerCreator<T> WithLogCriticalThatThrows() => WithLogOperationThatThrows<Exception>(LogLevel.Critical);

    public LoggerCreator<T> WithLogOperationThatThrows<TException>(LogLevel level) where TException : Exception, new()
    {
        _loggerMock.Setup(d => d.Log(level, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>() ,It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>())).Throws<TException>();
        return this;
    }
}
