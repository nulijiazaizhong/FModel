using System;
using CUE4Parse.UE4.Objects.Core.Misc;

namespace FModel.Views.Snooper;

public interface IDelayedSetup : IDisposable
{
    public FGuid Guid { get; }

    public void Setup();
}
