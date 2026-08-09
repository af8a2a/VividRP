#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

extern "C" std::uint32_t VividVT_ZstdVersionNumber();
extern "C" size_t VividVT_ZstdCompressBound(size_t sourceSize);
extern "C" size_t VividVT_ZstdCompress(
    void* destination,
    size_t destinationCapacity,
    const void* source,
    size_t sourceSize,
    int compressionLevel);
extern "C" size_t VividVT_ZstdDecompress(
    void* destination,
    size_t destinationCapacity,
    const void* source,
    size_t sourceSize);
extern "C" int VividVT_ZstdIsError(size_t code);

int main()
{
    if (VividVT_ZstdVersionNumber() != 10507u)
        return 1;

    std::array<std::uint8_t, 256 * 1024> source{};
    for (size_t index = 0; index < source.size(); ++index)
        source[index] = static_cast<std::uint8_t>(index % 19u);

    std::vector<std::uint8_t> compressed(VividVT_ZstdCompressBound(source.size()));
    const size_t compressedSize = VividVT_ZstdCompress(
        compressed.data(),
        compressed.size(),
        source.data(),
        source.size(),
        3);
    if (VividVT_ZstdIsError(compressedSize) != 0 || compressedSize >= source.size())
        return 2;

    std::array<std::uint8_t, 256 * 1024> decoded{};
    const size_t decodedSize = VividVT_ZstdDecompress(
        decoded.data(),
        decoded.size(),
        compressed.data(),
        compressedSize);
    if (VividVT_ZstdIsError(decodedSize) != 0 || decodedSize != source.size())
        return 3;
    return decoded == source ? 0 : 4;
}
