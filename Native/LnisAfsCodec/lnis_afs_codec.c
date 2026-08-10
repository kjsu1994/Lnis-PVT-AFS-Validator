#include "lnis_afs_codec.h"
#include <stddef.h>
#include <string.h>

/* Implemented by the unmodified upstream sources/libraries. */
void encode_LDPC_AFS_SF2(const uint8_t *syms, uint8_t *syms_enc);
void encode_LDPC_AFS_SF3(const uint8_t *syms, uint8_t *syms_enc);
void append_CRC24(uint8_t *syms, int len);
void generate_BCH_AFS_SF1(uint8_t *syms, int fid, int toi);
void interleave_AFS_SF234(uint8_t *syms_in, uint8_t *syms_out);
int sdr_decode_LDPC_AFS_SF2(const uint8_t *syms, uint8_t *syms_dec);
int sdr_decode_LDPC_AFS_SF3(const uint8_t *syms, uint8_t *syms_dec);

#define ERASURE 2
static _Thread_local char last_error[160];
static const uint8_t sync_bytes[9] = {0xCC,0x63,0xF7,0x45,0x36,0xF4,0x9E,0x04,0xA0};

static int fail(const char *message) {
    strncpy(last_error, message, sizeof(last_error) - 1);
    last_error[sizeof(last_error) - 1] = '\0';
    return -1;
}

static void unpack(const uint8_t *bytes, int bit_count, uint8_t *bits) {
    int i;
    for (i = 0; i < bit_count; ++i) bits[i] = (bytes[i >> 3] >> (7 - (i & 7))) & 1u;
}

static void pack(const uint8_t *bits, int bit_count, uint8_t *bytes) {
    int i;
    memset(bytes, 0, (size_t)((bit_count + 7) / 8));
    for (i = 0; i < bit_count; ++i) bytes[i >> 3] |= (uint8_t)((bits[i] & 1u) << (7 - (i & 7)));
}

static int valid_bits(const uint8_t *bits, uint32_t length) {
    uint32_t i;
    if (!bits) return 0;
    for (i = 0; i < length; ++i) if (bits[i] > 1u) return 0;
    return 1;
}

static int crc_ok(const uint8_t *decoded, int data_len, int total_len) {
    uint8_t copy[1200];
    memcpy(copy, decoded, (size_t)total_len);
    append_CRC24(copy, total_len);
    return memcmp(copy + data_len, decoded + data_len, 24) == 0;
}

uint32_t lnis_afs_get_abi_version(void) { return LNIS_AFS_ABI_VERSION; }
const char *lnis_afs_get_last_error(void) { return last_error; }

int32_t lnis_afs_encode_frame(uint8_t toi,
    const uint8_t *sb2, uint32_t sb2_len,
    const uint8_t *sb3, uint32_t sb3_len,
    const uint8_t *sb4, uint32_t sb4_len,
    uint8_t *frame_bytes, uint32_t frame_len) {
    uint8_t frame[LNIS_AFS_FRAME_BITS], coded[5880], interleaved[5880];
    uint8_t block[1200];
    if (toi >= 100) return fail("TOI must be in the range 0..99");
    if (sb2_len != 1176 || sb3_len != 846 || sb4_len != 846 || frame_len != 750)
        return fail("Invalid AFS buffer length");
    if (!frame_bytes || !valid_bits(sb2, sb2_len) || !valid_bits(sb3, sb3_len) || !valid_bits(sb4, sb4_len))
        return fail("AFS inputs must contain unpacked 0/1 bits");

    unpack(sync_bytes, 68, frame);
    generate_BCH_AFS_SF1(frame + 68, 0, toi);

    memset(block, 0, sizeof(block)); memcpy(block, sb2, 1176); append_CRC24(block, 1200);
    encode_LDPC_AFS_SF2(block, coded);
    memset(block, 0, sizeof(block)); memcpy(block, sb3, 846); append_CRC24(block, 870);
    encode_LDPC_AFS_SF3(block, coded + 2400);
    memset(block, 0, sizeof(block)); memcpy(block, sb4, 846); append_CRC24(block, 870);
    encode_LDPC_AFS_SF3(block, coded + 4140);
    interleave_AFS_SF234(coded, interleaved);
    memcpy(frame + 120, interleaved, 5880);
    pack(frame, 6000, frame_bytes);
    last_error[0] = '\0';
    return 0;
}

static int decode_sb34(const uint8_t *source, uint8_t *output, int *corrections) {
    uint8_t received[4576], decoded[3696];
    int i, result;
    for (i = 0; i < 176; ++i) received[i] = ERASURE;
    memcpy(received + 176, source, 694);
    for (i = 0; i < 10; ++i) received[870 + i] = ERASURE;
    memcpy(received + 880, source + 694, 1046);
    for (i = 1926; i < 4576; ++i) received[i] = ERASURE;
    result = sdr_decode_LDPC_AFS_SF3(received, decoded);
    *corrections = result;
    if (result < 0 || !crc_ok(decoded, 846, 870)) return 0;
    memcpy(output, decoded, 846);
    return 1;
}

int32_t lnis_afs_decode_frame(uint8_t toi,
    const uint8_t *frame_bytes, uint32_t frame_len,
    uint8_t *sb2, uint32_t sb2_len,
    uint8_t *sb3, uint32_t sb3_len,
    uint8_t *sb4, uint32_t sb4_len,
    lnis_afs_decode_status *status) {
    uint8_t frame[6000], sync[68], sf1[52], deinterleaved[5880];
    uint8_t received[6240], decoded[5040];
    int i, j, k, result;
    if (!frame_bytes || !sb2 || !sb3 || !sb4 || !status || frame_len != 750 ||
        sb2_len != 1176 || sb3_len != 846 || sb4_len != 846 || toi >= 100)
        return fail("Invalid AFS decode argument");
    memset(status, 0, sizeof(*status));
    unpack(frame_bytes, 6000, frame); unpack(sync_bytes, 68, sync);
    if (memcmp(frame, sync, 68) != 0) return fail("AFS synchronization pattern mismatch");
    generate_BCH_AFS_SF1(sf1, 0, toi);
    if (memcmp(frame + 68, sf1, 52) != 0) return fail("AFS SB1/TOI mismatch");
    for (i = 0, k = 0; i < 60; ++i) for (j = 0; j < 98; ++j)
        deinterleaved[k++] = frame[j * 60 + i + 120];

    for (i = 0; i < 240; ++i) received[i] = ERASURE;
    memcpy(received + 240, deinterleaved, 2400);
    for (i = 2640; i < 6240; ++i) received[i] = ERASURE;
    result = sdr_decode_LDPC_AFS_SF2(received, decoded);
    status->sb2_corrections = result;
    if (result >= 0 && crc_ok(decoded, 1176, 1200)) {
        status->sb2_ok = 1; memcpy(sb2, decoded, 1176);
    }
    status->sb3_ok = (uint8_t)decode_sb34(deinterleaved + 2400, sb3, &status->sb3_corrections);
    status->sb4_ok = (uint8_t)decode_sb34(deinterleaved + 4140, sb4, &status->sb4_corrections);
    last_error[0] = '\0';
    return (status->sb2_ok && status->sb3_ok && status->sb4_ok) ? 0 : 1;
}
