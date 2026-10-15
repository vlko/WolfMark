using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Zapisnik.App.Ui;

/// <summary>A view that can be re-bound to another item when recycled by <see cref="ItemsRepeater"/>.</summary>
internal interface IItemView<in TItem>
{
    void Bind(TItem item);
}

/// <summary>
/// Code-only element factory for virtualised <see cref="ItemsRepeater"/>s: creates views, recycles
/// them and re-binds to the new data item. No DataTemplate, no XAML, no reflection.
/// </summary>
internal sealed partial class ElementFactory<TItem, TView> : IElementFactory
    where TView : UIElement, IItemView<TItem>
{
    private readonly Func<TView> _create;
    private readonly Stack<TView> _pool = new();

    public ElementFactory(Func<TView> create) => _create = create;

    public UIElement GetElement(ElementFactoryGetArgs args)
    {
        var view = _pool.Count > 0 ? _pool.Pop() : _create();
        if (args.Data is TItem item)
        {
            view.Bind(item);
        }

        return view;
    }

    public void RecycleElement(ElementFactoryRecycleArgs args)
    {
        if (args.Element is TView view)
        {
            _pool.Push(view);
        }
    }
}

/// <summary>Chooses a factory per item (e.g. history headers vs. rows).</summary>
internal sealed partial class SelectingElementFactory(Func<object, IElementFactory> select) : IElementFactory
{
    private readonly Dictionary<UIElement, IElementFactory> _owners = [];

    public UIElement GetElement(ElementFactoryGetArgs args)
    {
        var factory = select(args.Data);
        var element = factory.GetElement(args);
        _owners[element] = factory;
        return element;
    }

    public void RecycleElement(ElementFactoryRecycleArgs args)
    {
        if (_owners.Remove(args.Element, out var factory))
        {
            factory.RecycleElement(args);
        }
    }
}
