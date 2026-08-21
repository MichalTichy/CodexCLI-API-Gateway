namespace CodexGateway.Logic.Specifications;

public interface ISpecification<in TSource, out TResult>
{
    TResult Apply(TSource source);
}
