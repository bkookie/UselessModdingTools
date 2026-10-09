using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace UselessModdingTools.IL;

internal class StackDeltaTypeProvider : ISignatureTypeProvider<int, object?>
{
    public int GetFunctionPointerType(MethodSignature<int> signature)
    {
        return 1;
    }

    public int GetGenericMethodParameter(object? genericContext, int index)
    {
        return 1;
    }

    public int GetGenericTypeParameter(object? genericContext, int index)
    {
        return 1;
    }

    public int GetModifiedType(int modifier, int unmodifiedType, bool isRequired)
    {
        return unmodifiedType; // modifer will never affect the stack
    }

    public int GetPinnedType(int elementType)
    {
        return 1;
    }

    public int GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        return 1;
    }

    public int GetArrayType(int elementType, ArrayShape shape)
    {
        return 1;
    }

    public int GetByReferenceType(int elementType)
    {
        return 1;
    }

    public int GetGenericInstantiation(int genericType, ImmutableArray<int> typeArguments)
    {
        return 1;
    }

    public int GetPointerType(int elementType)
    {
        return 1;
    }

    public int GetSZArrayType(int elementType)
    {
        return 1;
    }

    public int GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return typeCode == PrimitiveTypeCode.Void ? 0 : 1;
    }

    public int GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        return 1;
    }

    public int GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        return 1;
    }
}
