#ifndef LNIS_AFS_CODEC_H
#define LNIS_AFS_CODEC_H

#include <stdint.h>

#ifdef _WIN32
#define LNIS_API __declspec(dllexport)
#else
#define LNIS_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum {
    LNIS_AFS_ABI_VERSION = 1,
    LNIS_AFS_FRAME_BITS = 6000,
    LNIS_AFS_FRAME_BYTES = 750,
    LNIS_AFS_SB2_DATA_BITS = 1176,
    LNIS_AFS_SB34_DATA_BITS = 846
};

typedef struct {
    uint8_t sb2_ok;
    uint8_t sb3_ok;
    uint8_t sb4_ok;
    int32_t sb2_corrections;
    int32_t sb3_corrections;
    int32_t sb4_corrections;
} lnis_afs_decode_status;

LNIS_API uint32_t lnis_afs_get_abi_version(void);
LNIS_API const char *lnis_afs_get_last_error(void);

LNIS_API int32_t lnis_afs_encode_frame(
    uint8_t toi,
    const uint8_t *sb2_data_bits, uint32_t sb2_len,
    const uint8_t *sb3_data_bits, uint32_t sb3_len,
    const uint8_t *sb4_data_bits, uint32_t sb4_len,
    uint8_t *frame_bytes, uint32_t frame_len);

LNIS_API int32_t lnis_afs_decode_frame(
    uint8_t toi,
    const uint8_t *frame_bytes, uint32_t frame_len,
    uint8_t *sb2_data_bits, uint32_t sb2_len,
    uint8_t *sb3_data_bits, uint32_t sb3_len,
    uint8_t *sb4_data_bits, uint32_t sb4_len,
    lnis_afs_decode_status *status);

#ifdef __cplusplus
}
#endif
#endif
