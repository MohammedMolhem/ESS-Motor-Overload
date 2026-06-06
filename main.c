/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.c
  * @brief          : 3-Phase Motor Bearing & Stator Fault Detection
  *                   WeAct STM32H750VBT6 + ST7735 0.96" TFT (160x80)
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2026 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */
/* Includes ------------------------------------------------------------------*/
#include "main.h"
#include "usb_device.h"
#include "usbd_cdc_if.h"
#include "arm_math.h"
#include "math.h"
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <ctype.h>
/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */
#include "tft_display.h"
#include "fmt_double.h"
#include "esprit.h"
#include "spi1_flash.h"
#include "app_clock.h"
#include "qspi_assets.h"
#include <stdarg.h>
/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */

/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */

/* USER CODE END PD */

/* Private macro -------------------------------------------------------------*/
/* USER CODE BEGIN PM */

/* USER CODE END PM */

/* Private variables ---------------------------------------------------------*/
ADC_HandleTypeDef hadc1;
ADC_HandleTypeDef hadc2;
ADC_HandleTypeDef hadc3;
DMA_HandleTypeDef hdma_adc1;
DMA_HandleTypeDef hdma_adc2;
DMA_HandleTypeDef hdma_adc3;

QSPI_HandleTypeDef hqspi;

SPI_HandleTypeDef hspi1;
SPI_HandleTypeDef hspi4;

TIM_HandleTypeDef htim1;
TIM_HandleTypeDef htim2;

/* USER CODE BEGIN PV */

/* USER CODE END PV */

/* Private function prototypes -----------------------------------------------*/
void SystemClock_Config(void);
void PeriphCommonClock_Config(void);
static void MPU_Config(void);
static void MX_GPIO_Init(void);
static void MX_DMA_Init(void);
static void MX_QUADSPI_Init(void);
static void MX_ADC1_Init(void);
static void MX_ADC2_Init(void);
static void MX_ADC3_Init(void);
static void MX_SPI1_Init(void);
static void MX_SPI4_Init(void);
static void MX_TIM1_Init(void);
static void MX_TIM2_Init(void);
/* USER CODE BEGIN PFP */

/* USER CODE END PFP */

/* Private user code ---------------------------------------------------------*/
/* USER CODE BEGIN 0 */

/* ============================================================================
 * 3-Phase Motor Bearing Fault Detection
 * ============================================================================
 * Acquires 3 ADC channels (phase A/B/C) synchronized by TIM2_TRGO
 * Computes BPFO/BPFI/BSF/FTF from bearing geometry + shaft speed
 *
 * Detection paths:
 *   (1) LS sinusoid amplitudes at fault harmonics (Kim et al. TIE 2013)
 *   (2) Complex-exponential steering MUSIC (a_i = e^{jωi}/√L) on supply-cleaned residual
 *   (3) Teager-Kaiser energy on mean current + LS (AM/FM stress)
 *   (4) Local LS peak search +/-bandwidth for slip/RPM tolerance
 *   (5) Fast kurtogram: IIR band-pass -> Hilbert envelope -> excess kurtosis
 *       -> best band -> envelope-domain LS (Antoni MSSP 2007)
 *   (6) Wavelet: DWT only (no CWT) — db4, 5 levels, periodic extension
 *   (7) MCSA cyclostationary (2nd-order): |FFT(x^2)| lines at 2× fault / 2× supply vs baseline bands
 *   (8) Supply sideband LS at f_line ± k·f_fault (Kim MCSA sidebands)
 *   (9) Squared-envelope ACF peak at fault period (impact periodicity)
 *   (10) Per-phase max LS fusion (not mean-only); TLS-ESPRIT alongside LS-ESPRIT
 *   (11) Adaptive healthy-motor baseline (CALIB / SAVEBASE / LOADBASE → QSPI)
 *   (12) EMA + CUSUM on fused index for stable WARN/ALARM
 *   (13) Welch PSD: 50%-overlap Hamming segments, averaged |FFT|^2 (per-phase max)
 *   (14) Spectral coherence: min |Cxy(f)|^2 over phase pairs; fault index = coh_base/coh_fault
 *
 * Stator winding: Fortescue @ f/3f/5f; time-domain I0/I1/I2 RMS; phase spread;
 *   odd-harmonic index; fused short/gnd indices; EMA + CUSUM early warning (before WARN/ALARM)
 *
 * Neyman-Pearson: fixed Pfa per method on amplitude ratios — Fourier (LS), MUSIC, ESPRIT
 *   (F-test critical value from fault vs baseline sample counts); fused index + CUSUM.
 *
 * High-accuracy options (FP_HIGH_ACCURACY_EN): native |FFT| + LS Fourier fusion, shaft Hz
 *   from spectrum peak, 2-of-3 NP agreement for ALARM, detect_confidence score.
 *
 * Spectral resolution: one FFT frame spans T = N/fs seconds with bin spacing Δf = fs/N.
 *   (8192 @ 5 kHz → ~0.61 Hz, 1.64 s — high-accuracy build; CSV export uses full N/2 bins.)
 * ============================================================================
 */

#define FP_SAMPLES_PER_PHASE           8192u
#define FP_FFT_SIZE                    FP_SAMPLES_PER_PHASE
#define FP_HALF_FFT                    (FP_FFT_SIZE / 2u)

#define FP_SAMPLE_RATE_HZ              5000.0f

/* Preprocessor check: avoids _Static_assert squiggles in IDEs that parse as pre-C11. */
#if ((FP_SAMPLES_PER_PHASE & (FP_SAMPLES_PER_PHASE - 1u)) != 0u)
#error FP_SAMPLES_PER_PHASE must be a power of two (radix-2 FFT, dyadic DWT)
#endif

#define FP_ADC_RESOLUTION_F            4095.0f
#define FP_VREF                        3.3f
#define FP_CURRENT_SENSITIVITY         0.066f
#define FP_CURRENT_OFFSET              (FP_VREF / 2.0f)

#define FP_BASELINE_F1_HZ              50.0f
#define FP_BASELINE_F2_HZ              100.0f
#define FP_DEFAULT_BANDWIDTH_HZ        30.0f
#define FP_DEFAULT_HARMONIC_ORDERS     8u

/* Neyman-Pearson false alarm probability (lower = stricter alarm, fewer false positives) */
#define FP_NP_PFA                      1e-4f

/* High-accuracy bearing enhancements */
#define FP_HIGH_ACCURACY_EN              1
#define FP_SHAFT_TRACK_BLEND             0.35f   /* blend spectrum shaft estimate with RPM/slip */
#define FP_SHAFT_SPEC_FMIN_HZ            4.0f
#define FP_SHAFT_SPEC_FMAX_HZ            90.0f

/* Automatic slip / rotor-speed estimation from supply speed-sidebands (load-tracking).
 * f_r is found from the f_line ± f_r eccentricity sidebands; slip = 1 - f_r/f_sync. */
#define FP_SLIP_AUTO_EN                  1
#define FP_SLIP_MIN                      0.0f
#define FP_SLIP_MAX                      0.10f   /* search window: 0..10% slip */
#define FP_SLIP_SEARCH_STEPS             41u     /* coarse f_r grid across the slip window */
#define FP_SLIP_REFINE_STEPS             15u     /* local fine grid around the best candidate */
#define FP_SLIP_SIDEBAND_MIN_PROM        2.0f    /* best sideband pair / mean pair (peak prominence gate) */
#define FP_SLIP_TRACK_BLEND              0.5f    /* EMA blend of accepted slip estimates */
#define FP_FOURIER_LS_FFT_BLEND          1      /* index_ls = sqrt(LS_ratio * |FFT|_ratio) */
#define FP_NP_ALARM_MIN_VOTES            2u     /* need >=2 NP methods for ALARM (unless strong) */
#define FP_NP_STRONG_RATIO_MULT          2.5f   /* single-method ALARM if ratio > mult*gamma */
#define FP_DETECT_CONF_NP_WEIGHT         0.55f
#define FP_DETECT_CONF_PF_WEIGHT         0.45f

/* Subspace MUSIC / ESPRIT — larger L and M improve resolution at higher CPU cost */
#define FP_SUB_L                       32u
#define FP_SUB_M                       8u
#define FP_SUPPLY_LINE_HZ              50.0f
#define FP_SUPPLY_HARMONICS_REMOVE     8u

/* USB: 2048-bin grids for MUSIC/SK/GRAPHDATA; native N/2 bins for high-accuracy spectral CSV. */
#if (FP_HALF_FFT > 2048u)
#define USB_EXPORT_GRID_BINS           2048u
#else
#define USB_EXPORT_GRID_BINS           FP_HALF_FFT
#endif
#if FP_HIGH_ACCURACY_EN
#define USB_EXPORT_NATIVE_BINS         FP_HALF_FFT   /* Δf = fs/N (~1.22 Hz @ 8192, 5 kHz) */
#else
#define USB_EXPORT_NATIVE_BINS         USB_EXPORT_GRID_BINS
#endif

/* Place bulky buffers in SRAM2 (RAM_D2); ADC DMA stays in .dma_noncacheable. */
#define RAM_D2_BULK  __attribute__((section(".ram_d2_bulk"), aligned(32)))
#define USB_EXPORT_CSV_ROW_PAIRS       16u /* (f,mag) pairs per line; keep line < sizeof(usb_cdc_tx_buf) */
#define USB_FULL_SPECTRAL_EXPORT       1
#define USB_FULLREPORT_INCLUDE_SPECTRAL  1

/* Stator: EARLY < WARN < ALARM (EARLY = before hard fault, CUSUM + soft limits) */
#define FP_STATOR_NSR_EARLY            0.012f
#define FP_STATOR_NSR_WARN             0.028f
#define FP_STATOR_NSR_ALARM            0.085f
#define FP_STATOR_ZSR_EARLY            0.010f
#define FP_STATOR_ZSR_WARN             0.024f
#define FP_STATOR_ZSR_ALARM            0.075f
#define FP_STATOR_IMB_EARLY_PCT        2.5f
#define FP_STATOR_HARM_EARLY            0.032f
#define FP_STATOR_HARM_WARN            0.048f
#define FP_STATOR_HARM_ALARM           0.115f
#define FP_STATOR_RESID_EARLY          0.006f
#define FP_STATOR_RESID_WARN           0.009f
#define FP_STATOR_RESID_ALARM          0.030f
#define FP_STATOR_ZSR_H3_EARLY         0.028f
#define FP_STATOR_ZSR_H3_WARN          0.042f
#define FP_STATOR_ZSR_H3_ALARM         0.115f
#define FP_STATOR_NSR_TD_EARLY         0.010f
#define FP_STATOR_NSR_TD_WARN          0.022f
#define FP_STATOR_NSR_TD_ALARM         0.070f
#define FP_STATOR_ZSR_TD_EARLY         0.008f
#define FP_STATOR_ZSR_TD_WARN          0.020f
#define FP_STATOR_ZSR_TD_ALARM         0.065f
#define FP_STATOR_PHASE_SPREAD_EARLY   4.0f
#define FP_STATOR_PHASE_SPREAD_WARN    7.0f
#define FP_STATOR_PHASE_SPREAD_ALARM   15.0f
#define FP_STATOR_ODD_HARM_EARLY       0.028f
#define FP_STATOR_ODD_HARM_WARN        0.045f
#define FP_STATOR_ODD_HARM_ALARM       0.110f
#define FP_STATOR_SHORT_INDEX_EARLY    1.25f
#define FP_STATOR_SHORT_INDEX_WARN     1.55f
#define FP_STATOR_SHORT_INDEX_ALARM    2.20f
#define FP_STATOR_GND_INDEX_EARLY      1.22f
#define FP_STATOR_GND_INDEX_WARN       1.50f
#define FP_STATOR_GND_INDEX_ALARM      2.10f
#define FP_STATOR_INDEX_EMA_ALPHA      0.15f
#define FP_STATOR_CUSUM_DRIFT          0.03f
#define FP_STATOR_CUSUM_EARLY          2.5f
#define FP_STATOR_CUSUM_WARN           4.0f
#define FP_STATOR_CUSUM_ALARM          6.5f
#define FP_STATOR_INTEGRATE_FRAMES     6u

#define STATOR_BASELINE_MAGIC          0x5A0A7E01u
#define STATOR_BASELINE_N              10u

/* LS refinement — more steps = finer fault-frequency localization (latency ↑) */
#define FP_FAULT_FREQ_REFINE_STEPS     31u

/* Geometric fault_index: average fusion over this many consecutive frames (latency ↑, variance ↓) */
#define FP_FAULT_INDEX_INTEGRATE_FRAMES 8u

/* EMA smoothing on fused index (used for WARN/ALARM + CUSUM) */
#define FP_FAULT_INDEX_EMA_ALPHA         0.12f

/* CUSUM sequential detector on fault_index_ema */
#define FP_CUSUM_DRIFT                   0.04f
#define FP_CUSUM_ALARM                   5.0f

/* Healthy-motor baseline calibration (frames to average during CALIB) */
#define FP_BASELINE_CALIB_FRAMES         32u
#define BASELINE_MAGIC                   0xBA5E0001u
#define BASELINE_N_FEATURES              12u

/* Auto ADC offset when all phases RMS below this (A) on first captures */
#define FP_ADC_AUTOCAL_MAX_RMS_A         0.35f

/* Kurtogram bands — more bands = finer band selection (more envelope FFT work) */
#define FP_KURT_NBANDS                 12u
#define FP_KURT_FFT_N                  FP_FFT_SIZE
/* STFT spectral kurtosis segments (trade-off: statistics vs runtime). */
#define FP_SK_SEGMENTS                 8u
#if (FP_FFT_SIZE % FP_SK_SEGMENTS) != 0
#error FP_SK_SEGMENTS must divide FP_FFT_SIZE
#endif

/* Welch PSD: 50% overlap Hamming segments (more segments → smoother PSD / coherence). */
#define FP_WELCH_SEGMENTS              16u
#if (FP_FFT_SIZE % FP_WELCH_SEGMENTS) != 0
#error FP_WELCH_SEGMENTS must divide FP_FFT_SIZE
#endif
#define FP_WELCH_SEG_LEN               (FP_FFT_SIZE / FP_WELCH_SEGMENTS)
#define FP_WELCH_HOP_NUM               1u   /* hop = seg_len/2 → 50% overlap */
#define FP_WELCH_HOP_DEN               2u
#define FP_WELCH_HOP                   ((FP_WELCH_SEG_LEN * FP_WELCH_HOP_NUM) / FP_WELCH_HOP_DEN)
#if (FP_WELCH_SEG_LEN < 64u) || ((FP_WELCH_SEG_LEN & (FP_WELCH_SEG_LEN - 1u)) != 0u)
#error FP_WELCH_SEG_LEN must be a power of two >= 64
#endif

/* Wavelet levels */
#define FP_WAVELET_LEVELS              5u

/* ============================================================================
 * Global structures (shared with tft_display.c via extern in main.h)
 * ============================================================================
 */

BearingDetectParams_t g_params =
{
  .motor_rpm           = 1500.0f,
  .motor_slip          = 0.05f,
  .bpfo_coefficient    = 3.566f,
  .bpfi_coefficient    = 5.434f,
  .ftf_coefficient     = 0.383f,
  .bsf_coefficient     = 0.396f,
  .harmonic_orders     = FP_DEFAULT_HARMONIC_ORDERS,
  .bandwidth_hz        = FP_DEFAULT_BANDWIDTH_HZ,
  .fault_threshold     = 4.5f,
  .warning_threshold   = 2.0f
};

FaultDetectResult_t g_fault = {0};
float g_supply_line_hz = FP_SUPPLY_LINE_HZ;

/* ============================================================================
 * USB Parameter Control (CDC)
 * ============================================================================
 * Binary packet: Byte0=CMD, Bytes1..4=float32 (LE)
 * ASCII: key=value lines (CR/LF terminated); SET ack echoes quantized stored value (e.g. OK RPM=1500.000)
 *   PING GET GETCOEFF RPM= SLIP= SLIPAUTO=0/1 BPFO= BPFI= BSF= FTF= FTH= WTH= LINE= (GET adds DATE= TIME=)
 *   SLIPAUTO=1 (default): slip is estimated each frame from the supply speed-sidebands
 *   (f_line ± f_r) and tracks load; SLIP= is the fallback/nominal. REPORT shows SLIP/SLIP_OK/POLES.
 * BPFO/BPFI/BSF/FTF are dimensionless coefficients (not BPFO_HZ in [BEARING] / FULLREPORT).
 * Binary GET_ALL_PARAMS (0x80) float order: RPM, SLIP, BPFO, BPFI, BSF, FTF, FTH, WTH
 *   (matches text GET / FULLREPORT [PARAMS] and BearingDetectParams_t field order).
 *   DATE yyyy-mm-dd   TIME hh:mm[:ss]  (software clock, HAL tick)
 *   SAVE LOAD DEFAULT SAVEADC (ADC -> SPI1 NOR) HELP
 *   CALIB SAVEBASE LOADBASE CLEARBASE (healthy-motor baseline -> QSPI)
 *   CALIBST SAVESTST LOADSTST CLEARSTST (stator healthy baseline -> QSPI)
 *   PHASE3 / PHASECSV (3-phase time CSV); REPORT / FULLREPORT (all results);
 *   GRAPHDATA=native 4096 pts/phase + 2048 FFT bins for plots; FFTCSV WELCHCSV COHCSV ... (one technique each)
 */

#define USB_NATIVE_SPEC_BINS           FP_HALF_FFT
#define USB_GRAPH_PROGRESS_EVERY       512u

#define CMD_SET_RPM              0x04u
#define CMD_SET_BPFO             0x05u
#define CMD_SET_BPFI             0x06u
#define CMD_SET_FTF              0x07u
#define CMD_SET_BSF              0x08u
#define CMD_SET_FAULT_THRESH     0x09u
#define CMD_SET_WARNING_THRESH   0x0Au
#define CMD_SET_SLIP             0x0Bu
#define CMD_SET_STREAM           0x0Cu

#define CMD_GET_ALL_PARAMS       0x80u
#define CMD_SAVE_TO_FLASH        0x30u
#define CMD_LOAD_FROM_FLASH      0x31u
#define CMD_RESET_TO_DEFAULT     0x32u
#define CMD_SAVE_ADC_TO_SPI1    0x40u
#define CMD_PING                 0xFEu

typedef struct
{
  uint32_t magic;
  uint32_t n_frames;
  float mean[BASELINE_N_FEATURES];
  float var[BASELINE_N_FEATURES];
  uint32_t checksum;
} BaselineStats_t;

/* Feature order for baseline: ls, music, esprit, teager, sk, cyclic, sideband, acf, bpfo, bpfi, bsf, ftf */
enum {
  BL_LS = 0, BL_MU, BL_EP, BL_TG, BL_SK, BL_CY, BL_SB, BL_ACF,
  BL_BPFO, BL_BPFI, BL_BSF, BL_FTF
};

static volatile uint8_t g_baseline_calib_active = 0u;
static volatile uint32_t g_baseline_calib_count = 0u;
static uint8_t g_baseline_valid = 0u;
static BaselineStats_t g_baseline = {0};
static double g_baseline_acc[BASELINE_N_FEATURES];
static double g_baseline_acc2[BASELINE_N_FEATURES];

static float g_adc_offset_v[3] = { FP_CURRENT_OFFSET, FP_CURRENT_OFFSET, FP_CURRENT_OFFSET };
static uint8_t g_adc_autocal_done = 0u;
static float g_cusum_pos = 0.f;

/* Runtime enable for automatic slip estimation from supply speed-sidebands.
 * Toggle at runtime with the SLIPAUTO=0/1 USB command (not persisted; defaults on
 * when compiled with FP_SLIP_AUTO_EN). 0 forces the nominal SLIP= parameter. */
static uint8_t g_slip_auto_en = (FP_SLIP_AUTO_EN ? 1u : 0u);

#define USB_RX_MAX_BYTES         128u

/* Fixed decimal places for text SET/GET round-trip (quantize on store). */
#define USB_DEC_RPM              3u
#define USB_DEC_SLIP             4u
#define USB_DEC_COEFF            4u
#define USB_DEC_FTH              3u
#define USB_DEC_WTH              3u
#define USB_DEC_LINE             2u

/* QSPI external flash (W25Q64) */
#define QSPI_FLASH_SIZE          0x800000u
#define QSPI_SECTOR_SIZE         0x1000u
#define QSPI_PAGE_SIZE           256u
#define PARAM_STORAGE_ADDR       (QSPI_FLASH_SIZE - QSPI_SECTOR_SIZE)
#define BASELINE_STORAGE_ADDR    (QSPI_FLASH_SIZE - 2u * QSPI_SECTOR_SIZE)
#define STATOR_BASELINE_STORAGE_ADDR (QSPI_FLASH_SIZE - 3u * QSPI_SECTOR_SIZE)

typedef struct
{
  uint32_t magic;
  uint32_t n_frames;
  float mean[STATOR_BASELINE_N];
  float var[STATOR_BASELINE_N];
  uint32_t checksum;
} StatorBaselineStats_t;

/* nsr, zsr, nsr_td, zsr_td, imb, harm, resid, zsr_h3, odd_harm, phase_spread */
enum {
  SB_NSR = 0, SB_ZSR, SB_NSR_TD, SB_ZSR_TD, SB_IMB, SB_HARM,
  SB_RESID, SB_ZSR_H3, SB_ODD, SB_PSPREAD
};

static volatile uint8_t g_stator_calib_active = 0u;
static volatile uint32_t g_stator_calib_count = 0u;
static uint8_t g_stator_baseline_valid = 0u;
static StatorBaselineStats_t g_stator_baseline = {0};
static double g_stator_baseline_acc[STATOR_BASELINE_N];
static double g_stator_baseline_acc2[STATOR_BASELINE_N];
static float g_stator_cusum_short = 0.f;
static float g_stator_cusum_gnd = 0.f;

#define W25Q_CMD_WRITE_ENABLE    0x06u
#define W25Q_CMD_READ_STATUS1    0x05u
#define W25Q_CMD_READ_DATA       0x03u
#define W25Q_CMD_FAST_READ       0x0Bu
#define W25Q_CMD_PAGE_PROGRAM    0x02u
#define W25Q_CMD_SECTOR_ERASE    0x20u

#define W25Q_STATUS_BUSY         0x01u
#define W25Q_STATUS_WEL          0x02u
#define PARAM_MAGIC              0xDEADBEEFu

/* ADC snapshot — stored on SPI1 NOR (W25Qxx), not QSPI */
#define ADC_SNAPSHOT_MAGIC       0xADCC0DE1u
typedef struct
{
  uint32_t magic;
  uint32_t sample_rate_hz;
  uint32_t samples_per_channel;
  uint32_t adc_data_checksum;
} AdcSnapshotHeader_t;

#define ADC_SNAPSHOT_PAYLOAD_BYTES  ((uint32_t)sizeof(AdcSnapshotHeader_t) + \
                                     (uint32_t)(FP_SAMPLES_PER_PHASE * sizeof(uint16_t) * 3u))
#define ADC_SNAPSHOT_SPI1_NSECT    ((ADC_SNAPSHOT_PAYLOAD_BYTES + SPI1_FLASH_SECTOR_SIZE - 1u) / SPI1_FLASH_SECTOR_SIZE)
#define ADC_SNAPSHOT_SPI1_BASE     (SPI1_FLASH_SIZE - ADC_SNAPSHOT_SPI1_NSECT * SPI1_FLASH_SECTOR_SIZE)

static volatile uint8_t g_adc_save_requested = 0u;

static volatile uint8_t  g_usb_rx_ready = 0u;
static volatile uint32_t g_usb_rx_len   = 0u;
static uint8_t g_usb_rx_buf[USB_RX_MAX_BYTES] = {0};
/* Match APP_TX_DATA_SIZE in usbd_cdc_if.h for multi-line spectral reports */
static uint8_t usb_cdc_tx_buf[2048];

/* ============================================================================
 * Binary Fault Frame Streaming (STREAM=1)
 * ============================================================================
 * Compact binary frame for live monitoring (~260 bytes vs ~800+ text).
 * When g_usb_binary_stream = 1, USB_Send_FaultSummary sends this packed
 * struct instead of the multi-line text format. CSV exports (FFTCSV, etc.)
 * remain text — only the automatic per-frame summary switches to binary.
 * Toggle at runtime: STREAM=0 (text, default) / STREAM=1 (binary).
 * Binary CMD: [0x0C][0x00]=text or [0x0C][0x01]=binary.
 *
 * Wire format: [0xAA][0x55][CMD=0xF0][FaultBinaryFrame_t][0x55][0xAA]
 * All multi-byte fields are little-endian (native STM32).
 * CRC-16/CCITT covers all struct bytes before the crc16 field.
 * ============================================================================
 */

#define CMD_FAULT_BINARY_FRAME   0xF0u

#pragma pack(push, 1)
typedef struct {
  uint32_t frame_id;              /* sequential counter (monotonically increasing) */
  uint32_t timestamp_ms;          /* HAL_GetTick() at end of DSP frame */
  float    rpm;                   /* current motor RPM (from g_params) */
  float    slip;                  /* current slip (auto-estimated or nominal) */
  float    shaft_hz;              /* estimated shaft frequency */
  float    supply_line_hz;        /* AC line frequency */
  /* Shaft / slip tracking (auto-estimated from supply sidebands) */
  float    slip_estimated;        /* auto slip estimate (EMA), used for bearing fault freqs */
  uint8_t  slip_auto_valid;       /* 1 = speed-sideband slip accepted this frame */
  uint8_t  pole_pairs;            /* inferred pole-pair count (f_sync = f_line / pole_pairs) */
  /* Bearing fault frequencies (Hz) */
  float    bpfo_hz;
  float    bpfi_hz;
  float    bsf_hz;
  float    ftf_hz;
  /* 12 MCSA detection method indices */
  float    index_ls;              /* Fourier/LS sinusoid fit */
  float    index_fft;             /* native |FFT| ratio */
  float    index_music;           /* MUSIC pseudospectrum */
  float    index_esprit;          /* ESPRIT proxy */
  float    index_teager;          /* Teager-Kaiser energy */
  float    index_sk;              /* Spectral kurtosis */
  float    index_wavelet;         /* DWT wavelet */
  float    index_cyclic;          /* 2nd-order cyclostationary */
  float    index_sideband;        /* supply sideband */
  float    index_env_acf;         /* envelope ACF */
  float    index_welch;           /* Welch PSD */
  float    index_coherence;       /* spectral coherence */
  /* Spectral kurtosis peak (computed from SK export grid, matches SKPK in text summary) */
  float    sk_peak_kurtosis;      /* peak kurtosis value from fast kurtogram */
  float    sk_peak_f_hz;          /* frequency (Hz) at peak kurtosis */
  uint16_t kurt_band_fc_hz;       /* kurtogram filter-bank center frequency */
  /* 4 Bearing partial-fault indices */
  float    index_bpfo;
  float    index_bpfi;
  float    index_bsf;
  float    index_ftf;
  /* Fused bearing scores */
  float    fault_index;           /* instantaneous geometric fusion */
  float    fault_index_ema;       /* EMA-smoothed */
  float    cusum_score;           /* CUSUM sequential */
  float    detect_confidence;     /* 0..1 NP agreement */
  uint8_t  fault_level;           /* 0=OK 1=WARN 2=ALARM */
  uint8_t  dominant_fault;        /* 0=none 1=BPFO 2=BPFI 3=BSF 4=FTF */
  uint8_t  np_alarm_votes;        /* count of NP methods triggering alarm */
  uint8_t  np_alarm_fourier;
  uint8_t  np_alarm_music;
  uint8_t  np_alarm_esprit;
  /* NP (Neyman-Pearson) amplitudes — per-method fault vs baseline power */
  float    amp_fourier_fault;     /* mean LS (coherent Fourier) power @ fault lines */
  float    amp_fourier_base;      /* mean LS power @ baseline bands */
  float    amp_music_fault;       /* MUSIC pseudospectrum power @ fault */
  float    amp_music_base;        /* MUSIC pseudospectrum power @ baseline */
  float    amp_esprit_fault;      /* ESPRIT proxy power @ fault */
  float    amp_esprit_base;       /* ESPRIT proxy power @ baseline */
  /* NP F-test critical ratios (Pfa from FP_NP_PFA) */
  float    np_gamma_fourier;      /* F-test critical ratio for Fourier/LS */
  float    np_gamma_music;        /* F-test critical ratio for MUSIC */
  float    np_gamma_esprit;       /* F-test critical ratio for ESPRIT */
  /* Stator winding metrics — spectral symmetrical components */
  float    stator_i0_mag;         /* zero-sequence phasor magnitude */
  float    stator_i1_mag;         /* positive-sequence phasor magnitude */
  float    stator_i2_mag;         /* negative-sequence phasor magnitude */
  float    stator_nsr;            /* |I2|/|I1| spectral negative-sequence ratio */
  float    stator_zsr;            /* |I0|/|I1| spectral zero-sequence ratio */
  float    stator_imbalance_pct;  /* phase imbalance % */
  float    stator_harm_ratio;     /* harmonic ratio at 3f/5f */
  float    stator_resid_gnd_ratio;/* residual ground ratio */
  float    stator_zsr_h3;         /* ZSR at 3rd harmonic */
  float    stator_nsr_h5;         /* |I2|/|I1| at 5th harmonic (inter-turn indicator) */
  /* Stator winding metrics — time-domain symmetrical components */
  float    stator_i0_rms_td;      /* zero-sequence RMS (time-domain) */
  float    stator_i1_rms_td;      /* positive-sequence RMS (time-domain) */
  float    stator_i2_rms_td;      /* negative-sequence RMS (time-domain) */
  float    stator_nsr_td;         /* |I2|/|I1| RMS time-domain */
  float    stator_zsr_td;         /* |I0|/|I1| RMS time-domain */
  /* Stator winding metrics — composite indices */
  float    stator_odd_harm_index; /* (3f+5f+7f) odd harmonic inflation */
  float    stator_phase_spread_deg;/* max phase spread @ f (degrees) */
  float    stator_short_index;    /* fused short-circuit risk score */
  float    stator_gnd_index;      /* fused ground / insulation risk score */
  float    stator_short_index_ema;/* EMA-smoothed short index */
  float    stator_gnd_index_ema;  /* EMA-smoothed ground index */
  float    stator_cusum_short;    /* CUSUM on short index */
  float    stator_cusum_gnd;      /* CUSUM on ground index */
  /* Stator alarm / incipient flags */
  uint8_t  stator_fault_level;    /* 0=OK 1=EARLY 2=WARN 3=ALARM */
  uint8_t  stator_fault_short;    /* 0=OK 1=EARLY 2=WARN 3=ALARM */
  uint8_t  stator_fault_gnd;      /* 0=OK 1=EARLY 2=WARN 3=ALARM */
  uint8_t  stator_early_short;    /* 1 = incipient short detected before WARN */
  uint8_t  stator_early_gnd;      /* 1 = incipient ground detected before WARN */
  uint16_t crc16;                 /* CRC-16/CCITT over all preceding bytes */
} FaultBinaryFrame_t;
#pragma pack(pop)

/* Runtime toggle: 0 = text (default, backward-compatible), 1 = binary frame */
static uint8_t g_usb_binary_stream = 0u;
/* Frame counter for binary streaming */
static uint32_t g_usb_frame_counter = 0u;

/* Blocking USB-CDC sender: waits for the IN endpoint to drain so replies
 * are never dropped while the device is streaming. Defined later. */
static void USB_Tx_WaitSend(uint16_t nbytes);
static float RAM_D2_BULK g_usb_export_fft[USB_EXPORT_GRID_BINS];
static float RAM_D2_BULK g_usb_export_music[USB_EXPORT_GRID_BINS];
static float RAM_D2_BULK g_usb_export_cyclic2[USB_EXPORT_GRID_BINS];
static float RAM_D2_BULK g_usb_export_sk[USB_EXPORT_GRID_BINS];
static float g_cyclic2_psq[FP_HALF_FFT];
static float g_fft_mag_half[FP_HALF_FFT];
static float g_welch_psd_half[FP_HALF_FFT];
static float g_coh_min_half[FP_HALF_FFT];
static float RAM_D2_BULK g_phase_a_csv[FP_SAMPLES_PER_PHASE];
static float RAM_D2_BULK g_phase_b_csv[FP_SAMPLES_PER_PHASE];
static float RAM_D2_BULK g_phase_c_csv[FP_SAMPLES_PER_PHASE];
static volatile uint8_t g_phase_csv_ready = 0u;
static uint8_t g_qspi_ok = 0u;

typedef struct
{
  uint32_t magic;
  BearingDetectParams_t params;
  uint32_t checksum;
} PersistedParams_t;

/* ============================================================================
 * Input Validation Helpers
 * ============================================================================
 * Prevents NaN, Inf, negative, or out-of-range values from corrupting
 * the signal processing pipeline. Applied to both binary and ASCII commands.
 */
static uint8_t Validate_RPM(float v)
{
  return (isfinite(v) && v > 0.0f && v < 50000.0f) ? 1u : 0u;
}

static uint8_t Validate_Slip(float v)
{
  return (isfinite(v) && v >= 0.0f && v <= 1.0f) ? 1u : 0u;
}

static uint8_t Validate_Coefficient(float v)
{
  return (isfinite(v) && v > 0.0f && v < 100.0f) ? 1u : 0u;
}

static uint8_t Validate_Threshold(float v)
{
  return (isfinite(v) && v > 0.0f && v < 1000.0f) ? 1u : 0u;
}

#define USB_BINARY_N_PARAMS  8u

/* Binary GET_ALL_PARAMS wire order (same as text GET / struct field order). */
enum {
  USB_BIN_RPM = 0,
  USB_BIN_SLIP,
  USB_BIN_BPFO,
  USB_BIN_BPFI,
  USB_BIN_BSF,
  USB_BIN_FTF,
  USB_BIN_FTH,
  USB_BIN_WTH
};

static void Params_FillBinaryExport(float out[USB_BINARY_N_PARAMS])
{
  out[USB_BIN_RPM]  = g_params.motor_rpm;
  out[USB_BIN_SLIP] = g_params.motor_slip;
  out[USB_BIN_BPFO] = g_params.bpfo_coefficient;
  out[USB_BIN_BPFI] = g_params.bpfi_coefficient;
  out[USB_BIN_BSF]  = g_params.bsf_coefficient;
  out[USB_BIN_FTF]  = g_params.ftf_coefficient;
  out[USB_BIN_FTH]  = g_params.fault_threshold;
  out[USB_BIN_WTH]  = g_params.warning_threshold;
}

/* ============================================================================
 * Parameter persistence (QSPI W25Q64)
 * ============================================================================
 */

static uint32_t Params_Checksum32(const PersistedParams_t *p)
{
  const uint8_t *b = (const uint8_t *)p;
  uint32_t sum = 0u;
  for (uint32_t i = 4u; i < (uint32_t)(sizeof(PersistedParams_t) - 4u); i++)
    sum += b[i];
  return sum;
}

static float usb_read_f32_le(const uint8_t *p)
{
  uint32_t u = ((uint32_t)p[0])       |
               ((uint32_t)p[1] << 8)  |
               ((uint32_t)p[2] << 16) |
               ((uint32_t)p[3] << 24);
  float f = 0.0f;
  (void)memcpy(&f, &u, sizeof(float));
  return f;
}

/* Round to fixed decimals so text SET ack and GET always match. */
static float USB_QuantizeDec(float v, unsigned dec)
{
  if (dec > 6u) dec = 6u;
  float m = 1.f;
  for (unsigned i = 0u; i < dec; i++) m *= 10.f;
  return roundf(v * m) / m;
}

static void USB_NormalizeStoredParams(void)
{
  g_params.motor_rpm           = USB_QuantizeDec(g_params.motor_rpm, USB_DEC_RPM);
  g_params.motor_slip          = USB_QuantizeDec(g_params.motor_slip, USB_DEC_SLIP);
  g_params.bpfo_coefficient    = USB_QuantizeDec(g_params.bpfo_coefficient, USB_DEC_COEFF);
  g_params.bpfi_coefficient    = USB_QuantizeDec(g_params.bpfi_coefficient, USB_DEC_COEFF);
  g_params.bsf_coefficient     = USB_QuantizeDec(g_params.bsf_coefficient, USB_DEC_COEFF);
  g_params.ftf_coefficient     = USB_QuantizeDec(g_params.ftf_coefficient, USB_DEC_COEFF);
  g_params.fault_threshold     = USB_QuantizeDec(g_params.fault_threshold, USB_DEC_FTH);
  g_params.warning_threshold   = USB_QuantizeDec(g_params.warning_threshold, USB_DEC_WTH);
  g_supply_line_hz             = USB_QuantizeDec(g_supply_line_hz, USB_DEC_LINE);
}

static void USB_Send_Ack(uint8_t cmd, uint8_t status)
{
  usb_cdc_tx_buf[0] = 0xAA;
  usb_cdc_tx_buf[1] = 0x55;
  usb_cdc_tx_buf[2] = cmd;
  usb_cdc_tx_buf[3] = status;
  usb_cdc_tx_buf[4] = 0x55;
  usb_cdc_tx_buf[5] = 0xAA;
  USB_Tx_WaitSend(6u);
}

static void USB_Send_AllParams(void)
{
  uint32_t idx = 0u;
  usb_cdc_tx_buf[idx++] = 0xAA;
  usb_cdc_tx_buf[idx++] = 0x55;
  usb_cdc_tx_buf[idx++] = CMD_GET_ALL_PARAMS;

  uint32_t u32;
  float vals[USB_BINARY_N_PARAMS];
  Params_FillBinaryExport(vals);
  for (uint32_t i = 0u; i < USB_BINARY_N_PARAMS; i++)
  {
    (void)memcpy(&u32, &vals[i], sizeof(uint32_t));
    usb_cdc_tx_buf[idx++] = (uint8_t)(u32 & 0xFFu);
    usb_cdc_tx_buf[idx++] = (uint8_t)((u32 >> 8)  & 0xFFu);
    usb_cdc_tx_buf[idx++] = (uint8_t)((u32 >> 16) & 0xFFu);
    usb_cdc_tx_buf[idx++] = (uint8_t)((u32 >> 24) & 0xFFu);
  }

  usb_cdc_tx_buf[idx++] = 0x55;
  usb_cdc_tx_buf[idx++] = 0xAA;
  USB_Tx_WaitSend((uint16_t)idx);
}

static void Params_Load_Defaults(void)
{
  g_params.motor_rpm           = 1500.0f;
  g_params.motor_slip          = 0.05f;
  g_params.bpfo_coefficient    = 3.566f;
  g_params.bpfi_coefficient    = 5.434f;
  g_params.ftf_coefficient     = 0.383f;
  g_params.bsf_coefficient     = 0.396f;
  g_params.harmonic_orders     = FP_DEFAULT_HARMONIC_ORDERS;
  g_params.bandwidth_hz        = FP_DEFAULT_BANDWIDTH_HZ;
  g_params.fault_threshold     = 4.5f;
  g_params.warning_threshold   = 2.0f;
  g_supply_line_hz             = FP_SUPPLY_LINE_HZ;
  USB_NormalizeStoredParams();
}

/* ============================================================================
 * QSPI Flash driver (W25Q64)
 * ============================================================================
 */

static uint8_t QSPI_WriteEnable(void)
{
  QSPI_CommandTypeDef cmd = {0};
  QSPI_AutoPollingTypeDef poll = {0};

  cmd.InstructionMode = QSPI_INSTRUCTION_1_LINE;
  cmd.Instruction     = W25Q_CMD_WRITE_ENABLE;
  cmd.AddressMode     = QSPI_ADDRESS_NONE;
  cmd.DataMode        = QSPI_DATA_NONE;
  cmd.DummyCycles     = 0;
  cmd.DdrMode         = QSPI_DDR_MODE_DISABLE;
  cmd.SIOOMode        = QSPI_SIOO_INST_EVERY_CMD;
  if (HAL_QSPI_Command(&hqspi, &cmd, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK) return 0u;

  cmd.Instruction     = W25Q_CMD_READ_STATUS1;
  cmd.DataMode        = QSPI_DATA_1_LINE;
  cmd.NbData          = 1;
  poll.Match          = W25Q_STATUS_WEL;
  poll.Mask           = W25Q_STATUS_WEL;
  poll.MatchMode      = QSPI_MATCH_MODE_AND;
  poll.StatusBytesSize = 1;
  poll.Interval       = 0x10;
  poll.AutomaticStop  = QSPI_AUTOMATIC_STOP_ENABLE;
  return (HAL_QSPI_AutoPolling(&hqspi, &cmd, &poll, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) == HAL_OK) ? 1u : 0u;
}

static uint8_t QSPI_WaitReady(uint32_t timeout_ms)
{
  QSPI_CommandTypeDef cmd = {0};
  QSPI_AutoPollingTypeDef poll = {0};

  cmd.InstructionMode = QSPI_INSTRUCTION_1_LINE;
  cmd.Instruction     = W25Q_CMD_READ_STATUS1;
  cmd.AddressMode     = QSPI_ADDRESS_NONE;
  cmd.DataMode        = QSPI_DATA_1_LINE;
  cmd.NbData          = 1;
  cmd.DummyCycles     = 0;
  cmd.DdrMode         = QSPI_DDR_MODE_DISABLE;
  cmd.SIOOMode        = QSPI_SIOO_INST_EVERY_CMD;

  poll.Match          = 0u;
  poll.Mask           = W25Q_STATUS_BUSY;
  poll.MatchMode      = QSPI_MATCH_MODE_AND;
  poll.StatusBytesSize = 1;
  poll.Interval       = 0x10;
  poll.AutomaticStop  = QSPI_AUTOMATIC_STOP_ENABLE;
  return (HAL_QSPI_AutoPolling(&hqspi, &cmd, &poll, timeout_ms) == HAL_OK) ? 1u : 0u;
}

static uint8_t QSPI_ReadData(uint32_t address, uint8_t *data, uint32_t length)
{
  QSPI_LeaveMemoryMapped();
  QSPI_CommandTypeDef cmd = {0};
  cmd.InstructionMode = QSPI_INSTRUCTION_1_LINE;
  cmd.Instruction     = W25Q_CMD_FAST_READ;
  cmd.AddressMode     = QSPI_ADDRESS_1_LINE;
  cmd.AddressSize     = QSPI_ADDRESS_24_BITS;
  cmd.Address         = address;
  cmd.DataMode        = QSPI_DATA_1_LINE;
  cmd.NbData          = length;
  cmd.DummyCycles     = 8;
  cmd.DdrMode         = QSPI_DDR_MODE_DISABLE;
  cmd.SIOOMode        = QSPI_SIOO_INST_EVERY_CMD;

  if (HAL_QSPI_Command(&hqspi, &cmd, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK) {
    QSPI_EnterMemoryMapped();
    return 0u;
  }
  const uint8_t ok = (HAL_QSPI_Receive(&hqspi, data, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) == HAL_OK) ? 1u : 0u;
  QSPI_EnterMemoryMapped();
  return ok;
}

static uint8_t QSPI_EraseSector(uint32_t address)
{
  QSPI_LeaveMemoryMapped();
  QSPI_CommandTypeDef cmd = {0};
  if (!QSPI_WriteEnable()) return 0u;

  cmd.InstructionMode = QSPI_INSTRUCTION_1_LINE;
  cmd.Instruction     = W25Q_CMD_SECTOR_ERASE;
  cmd.AddressMode     = QSPI_ADDRESS_1_LINE;
  cmd.AddressSize     = QSPI_ADDRESS_24_BITS;
  cmd.Address         = address;
  cmd.DataMode        = QSPI_DATA_NONE;
  cmd.DummyCycles     = 0;
  cmd.DdrMode         = QSPI_DDR_MODE_DISABLE;
  cmd.SIOOMode        = QSPI_SIOO_INST_EVERY_CMD;

  if (HAL_QSPI_Command(&hqspi, &cmd, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK) {
    QSPI_EnterMemoryMapped();
    return 0u;
  }
  const uint8_t ok = QSPI_WaitReady(1000u);
  QSPI_EnterMemoryMapped();
  return ok;
}

static uint8_t QSPI_WritePage(uint32_t address, const uint8_t *data, uint16_t length)
{
  QSPI_LeaveMemoryMapped();
  QSPI_CommandTypeDef cmd = {0};
  if (length == 0u || length > QSPI_PAGE_SIZE) return 0u;
  if (!QSPI_WriteEnable()) return 0u;

  cmd.InstructionMode = QSPI_INSTRUCTION_1_LINE;
  cmd.Instruction     = W25Q_CMD_PAGE_PROGRAM;
  cmd.AddressMode     = QSPI_ADDRESS_1_LINE;
  cmd.AddressSize     = QSPI_ADDRESS_24_BITS;
  cmd.Address         = address;
  cmd.DataMode        = QSPI_DATA_1_LINE;
  cmd.NbData          = length;
  cmd.DummyCycles     = 0;
  cmd.DdrMode         = QSPI_DDR_MODE_DISABLE;
  cmd.SIOOMode        = QSPI_SIOO_INST_EVERY_CMD;

  if (HAL_QSPI_Command(&hqspi, &cmd, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK) {
    QSPI_EnterMemoryMapped();
    return 0u;
  }
  if (HAL_QSPI_Transmit(&hqspi, (uint8_t *)data, HAL_QSPI_TIMEOUT_DEFAULT_VALUE) != HAL_OK) {
    QSPI_EnterMemoryMapped();
    return 0u;
  }
  const uint8_t ok = QSPI_WaitReady(100u);
  QSPI_EnterMemoryMapped();
  return ok;
}

static uint8_t QSPI_WriteData(uint32_t address, const uint8_t *data, uint32_t length)
{
  uint32_t addr = address;
  const uint8_t *p = data;
  uint32_t rem = length;
  while (rem > 0u)
  {
    uint16_t page_off = (uint16_t)(addr % QSPI_PAGE_SIZE);
    uint16_t chunk    = (uint16_t)(QSPI_PAGE_SIZE - page_off);
    if (chunk > rem) chunk = (uint16_t)rem;
    if (!QSPI_WritePage(addr, p, chunk)) return 0u;
    addr += chunk;
    p    += chunk;
    rem  -= chunk;
  }
  return 1u;
}

static uint8_t Params_Save_To_QSPI(void)
{
  if (!g_qspi_ok) return 0u;
  PersistedParams_t p = {0};
  PersistedParams_t v = {0};
  p.magic    = PARAM_MAGIC;
  p.params   = g_params;
  p.checksum = Params_Checksum32(&p);

  if (!QSPI_EraseSector(PARAM_STORAGE_ADDR)) return 0u;
  if (!QSPI_WriteData(PARAM_STORAGE_ADDR, (const uint8_t *)&p, sizeof(p))) return 0u;
  if (!QSPI_ReadData(PARAM_STORAGE_ADDR, (uint8_t *)&v, sizeof(v))) return 0u;
  return (memcmp(&p, &v, sizeof(p)) == 0) ? 1u : 0u;
}

static uint8_t Params_Load_From_QSPI(void)
{
  if (!g_qspi_ok) return 0u;
  PersistedParams_t p = {0};
  if (!QSPI_ReadData(PARAM_STORAGE_ADDR, (uint8_t *)&p, sizeof(p))) return 0u;
  if (p.magic != PARAM_MAGIC) return 0u;
  if (Params_Checksum32(&p) != p.checksum) return 0u;
  g_params = p.params;
  USB_NormalizeStoredParams();
  return 1u;
}

static uint32_t ByteSum32(const uint8_t *data, uint32_t len)
{
  uint32_t sum = 0u;
  for (uint32_t i = 0u; i < len; i++) sum += (uint32_t)data[i];
  return sum;
}

static uint32_t Baseline_Checksum32(const BaselineStats_t *b)
{
  const uint8_t *p = (const uint8_t *)b;
  uint32_t sum = 0u;
  for (uint32_t i = 8u; i < (uint32_t)(sizeof(BaselineStats_t) - 4u); i++)
    sum += p[i];
  return sum;
}

static void Baseline_FeaturesFromFault(const float pf[4], float out[BASELINE_N_FEATURES])
{
  out[BL_LS]   = g_fault.index_ls;
  out[BL_MU]   = g_fault.index_music;
  out[BL_EP]   = g_fault.index_esprit;
  out[BL_TG]   = g_fault.index_teager;
  out[BL_SK]   = g_fault.index_sk;
  out[BL_CY]   = g_fault.index_cyclic;
  out[BL_SB]   = g_fault.index_sideband;
  out[BL_ACF]  = g_fault.index_env_acf;
  out[BL_BPFO] = pf[0];
  out[BL_BPFI] = pf[1];
  out[BL_BSF]  = pf[2];
  out[BL_FTF]  = pf[3];
}

static void Baseline_AccumulateFrame(const float pf[4])
{
  float f[BASELINE_N_FEATURES];
  Baseline_FeaturesFromFault(pf, f);
  for (uint32_t i = 0u; i < BASELINE_N_FEATURES; i++) {
    const double v = (double)f[i];
    g_baseline_acc[i]  += v;
    g_baseline_acc2[i] += v * v;
  }
  g_baseline_calib_count++;
}

static void Baseline_FinalizeFromAccum(void)
{
  const uint32_t n = g_baseline_calib_count;
  if (n < 4u) return;
  g_baseline.magic = BASELINE_MAGIC;
  g_baseline.n_frames = n;
  for (uint32_t i = 0u; i < BASELINE_N_FEATURES; i++) {
    const double mu = g_baseline_acc[i] / (double)n;
    const double v2 = g_baseline_acc2[i] / (double)n - mu * mu;
    g_baseline.mean[i] = (float)mu;
    g_baseline.var[i]  = (float)fmax(v2, 1e-8);
  }
  g_baseline.checksum = Baseline_Checksum32(&g_baseline);
  g_baseline_valid = 1u;
}

static uint8_t Baseline_Save_To_QSPI(void)
{
  if (!g_qspi_ok || !g_baseline_valid) return 0u;
  g_baseline.checksum = Baseline_Checksum32(&g_baseline);
  if (!QSPI_EraseSector(BASELINE_STORAGE_ADDR)) return 0u;
  if (!QSPI_WriteData(BASELINE_STORAGE_ADDR, (const uint8_t *)&g_baseline, sizeof(g_baseline))) return 0u;
  BaselineStats_t v = {0};
  if (!QSPI_ReadData(BASELINE_STORAGE_ADDR, (uint8_t *)&v, sizeof(v))) return 0u;
  return (memcmp(&g_baseline, &v, sizeof(g_baseline)) == 0) ? 1u : 0u;
}

static uint8_t Baseline_Load_From_QSPI(void)
{
  if (!g_qspi_ok) return 0u;
  BaselineStats_t b = {0};
  if (!QSPI_ReadData(BASELINE_STORAGE_ADDR, (uint8_t *)&b, sizeof(b))) return 0u;
  if (b.magic != BASELINE_MAGIC) return 0u;
  if (Baseline_Checksum32(&b) != b.checksum) return 0u;
  g_baseline = b;
  g_baseline_valid = 1u;
  return 1u;
}

static float Baseline_Normalize(uint32_t idx, float raw_ratio)
{
  if (!g_baseline_valid || idx >= BASELINE_N_FEATURES) return raw_ratio;
  const float mu = g_baseline.mean[idx];
  const float sig = sqrtf(g_baseline.var[idx]);
  if (mu <= 1e-12f) return raw_ratio;
  const float z = (raw_ratio - mu) / fmaxf(sig, 1e-6f);
  return fmaxf(raw_ratio / mu, 1.f + 0.25f * z);
}

static uint32_t StatorBaseline_Checksum32(const StatorBaselineStats_t *b)
{
  const uint8_t *p = (const uint8_t *)b;
  uint32_t sum = 0u;
  for (uint32_t i = 8u; i < (uint32_t)(sizeof(StatorBaselineStats_t) - 4u); i++)
    sum += p[i];
  return sum;
}

static void StatorBaseline_Features(float out[STATOR_BASELINE_N])
{
  out[SB_NSR]      = g_fault.stator_nsr;
  out[SB_ZSR]      = g_fault.stator_zsr;
  out[SB_NSR_TD]   = g_fault.stator_nsr_td;
  out[SB_ZSR_TD]   = g_fault.stator_zsr_td;
  out[SB_IMB]      = g_fault.stator_imbalance_pct;
  out[SB_HARM]     = g_fault.stator_harm_ratio;
  out[SB_RESID]    = g_fault.stator_resid_gnd_ratio;
  out[SB_ZSR_H3]   = g_fault.stator_zsr_h3;
  out[SB_ODD]      = g_fault.stator_odd_harm_index;
  out[SB_PSPREAD]  = g_fault.stator_phase_spread_deg;
}

static void StatorBaseline_Accumulate(void)
{
  float f[STATOR_BASELINE_N];
  StatorBaseline_Features(f);
  for (uint32_t i = 0u; i < STATOR_BASELINE_N; i++) {
    const double v = (double)f[i];
    g_stator_baseline_acc[i]  += v;
    g_stator_baseline_acc2[i] += v * v;
  }
  g_stator_calib_count++;
}

static void StatorBaseline_Finalize(void)
{
  const uint32_t n = g_stator_calib_count;
  if (n < 4u) return;
  g_stator_baseline.magic = STATOR_BASELINE_MAGIC;
  g_stator_baseline.n_frames = n;
  for (uint32_t i = 0u; i < STATOR_BASELINE_N; i++) {
    const double mu = g_stator_baseline_acc[i] / (double)n;
    const double v2 = g_stator_baseline_acc2[i] / (double)n - mu * mu;
    g_stator_baseline.mean[i] = (float)mu;
    g_stator_baseline.var[i]  = (float)fmax(v2, 1e-10);
  }
  g_stator_baseline.checksum = StatorBaseline_Checksum32(&g_stator_baseline);
  g_stator_baseline_valid = 1u;
}

static uint8_t StatorBaseline_Save_To_QSPI(void)
{
  if (!g_qspi_ok || !g_stator_baseline_valid) return 0u;
  g_stator_baseline.checksum = StatorBaseline_Checksum32(&g_stator_baseline);
  if (!QSPI_EraseSector(STATOR_BASELINE_STORAGE_ADDR)) return 0u;
  if (!QSPI_WriteData(STATOR_BASELINE_STORAGE_ADDR, (const uint8_t *)&g_stator_baseline,
                     sizeof(g_stator_baseline))) return 0u;
  StatorBaselineStats_t v = {0};
  if (!QSPI_ReadData(STATOR_BASELINE_STORAGE_ADDR, (uint8_t *)&v, sizeof(v))) return 0u;
  return (memcmp(&g_stator_baseline, &v, sizeof(g_stator_baseline)) == 0) ? 1u : 0u;
}

static uint8_t StatorBaseline_Load_From_QSPI(void)
{
  if (!g_qspi_ok) return 0u;
  StatorBaselineStats_t b = {0};
  if (!QSPI_ReadData(STATOR_BASELINE_STORAGE_ADDR, (uint8_t *)&b, sizeof(b))) return 0u;
  if (b.magic != STATOR_BASELINE_MAGIC) return 0u;
  if (StatorBaseline_Checksum32(&b) != b.checksum) return 0u;
  g_stator_baseline = b;
  g_stator_baseline_valid = 1u;
  return 1u;
}

static float StatorBaseline_Ratio(uint32_t idx, float val)
{
  if (!g_stator_baseline_valid || idx >= STATOR_BASELINE_N) return val;
  const float mu = g_stator_baseline.mean[idx];
  if (mu <= 1e-12f) return val;
  const float sig = sqrtf(g_stator_baseline.var[idx]);
  const float z = (val - mu) / fmaxf(sig, 1e-8f);
  return fmaxf(val / mu, 1.f + 0.35f * z);
}

static uint8_t Stator_MaxU8(uint8_t a, uint8_t b)
{
  return (a > b) ? a : b;
}

static uint8_t Stator_MetricLevel(float v, float early, float warn, float alarm)
{
  if (v >= alarm) return 3u;
  if (v >= warn)  return 2u;
  if (v >= early) return 1u;
  return 0u;
}

/* ============================================================================
 * ADC buffers and snapshot
 * ============================================================================
 */

static uint16_t raw_adc1[FP_SAMPLES_PER_PHASE] __attribute__((aligned(32), section(".dma_noncacheable")));
static uint16_t raw_adc2[FP_SAMPLES_PER_PHASE] __attribute__((aligned(32), section(".dma_noncacheable")));
static uint16_t raw_adc3[FP_SAMPLES_PER_PHASE] __attribute__((aligned(32), section(".dma_noncacheable")));

uint8_t g_ram_d3_data_storage[WEACT_RAM_D3_STORAGE_BYTES]
  __attribute__((section(".ram_d3_storage"), aligned(32)));

static uint8_t ADC_SaveSnapshot_To_SPI1Flash(void)
{
  if (!SPI1Flash_IsReady()) return 0u;

  const uint32_t adc_bytes = (uint32_t)(FP_SAMPLES_PER_PHASE * sizeof(uint16_t) * 3u);
  const uint8_t *raw_bytes = (const uint8_t *)raw_adc1;
  const uint32_t checksum  = ByteSum32(raw_bytes, adc_bytes);

  AdcSnapshotHeader_t hdr = {0};
  hdr.magic               = ADC_SNAPSHOT_MAGIC;
  hdr.sample_rate_hz      = (uint32_t)(FP_SAMPLE_RATE_HZ + 0.5f);
  hdr.samples_per_channel = FP_SAMPLES_PER_PHASE;
  hdr.adc_data_checksum   = checksum;

  if (!SPI1Flash_EraseSectors(ADC_SNAPSHOT_SPI1_BASE, ADC_SNAPSHOT_SPI1_NSECT)) return 0u;

  uint32_t addr = ADC_SNAPSHOT_SPI1_BASE;
  if (!SPI1Flash_Write(addr, (const uint8_t *)&hdr, (uint32_t)sizeof(hdr))) return 0u;
  addr += (uint32_t)sizeof(hdr);

  if (!SPI1Flash_Write(addr, (const uint8_t *)raw_adc1,
                      (uint32_t)(FP_SAMPLES_PER_PHASE * sizeof(uint16_t)))) return 0u;
  addr += (uint32_t)(FP_SAMPLES_PER_PHASE * sizeof(uint16_t));

  if (!SPI1Flash_Write(addr, (const uint8_t *)raw_adc2,
                      (uint32_t)(FP_SAMPLES_PER_PHASE * sizeof(uint16_t)))) return 0u;
  addr += (uint32_t)(FP_SAMPLES_PER_PHASE * sizeof(uint16_t));

  if (!SPI1Flash_Write(addr, (const uint8_t *)raw_adc3,
                      (uint32_t)(FP_SAMPLES_PER_PHASE * sizeof(uint16_t)))) return 0u;

  return 1u;
}

/* ============================================================================
 * USB text/binary command processing (with input validation)
 * ============================================================================
 */

static uint8_t USB_LooksLikeText(const uint8_t *buf, uint32_t len)
{
  if (len == 0u) return 0u;
  const uint8_t c = buf[0];
  return (c >= 0x20u && c <= 0x7Eu) ? 1u : 0u;
}

static void USB_Send_TextLine(const char *s)
{
  size_t n = strlen(s);
  if (n >= sizeof(usb_cdc_tx_buf)) n = sizeof(usb_cdc_tx_buf) - 1u;
  memcpy(usb_cdc_tx_buf, s, n);
  usb_cdc_tx_buf[n] = '\0';
  USB_Tx_WaitSend((uint16_t)n);
}

/* Text SET ack with stored value readback (coefficients / thresholds). */
static void USB_Send_TextOkFloat(const char *key, float value, unsigned dec)
{
  char *p = (char *)usb_cdc_tx_buf;
  char *const pe = p + sizeof(usb_cdc_tx_buf);
  size_t room = (size_t)(pe - p);
  int r = snprintf(p, room, "OK %s=", key);
  if (r < 0) { USB_Send_TextLine("OK\r\n"); return; }
  size_t off = (size_t)r;
  if (off >= room) { USB_Send_TextLine("OK\r\n"); return; }
  room = (size_t)(pe - p) - off;
  size_t w = fmt_double(p + off, room, (double)value, dec);
  if (w >= room) { USB_Send_TextLine("OK\r\n"); return; }
  off += w;
  if (off + 2u >= (size_t)(pe - p)) { USB_Send_TextLine("OK\r\n"); return; }
  p[off++] = '\r';
  p[off++] = '\n';
  USB_Tx_WaitSend((uint16_t)off);
}

static void USB_Send_SpectralFigures(void);
static void USB_Send_PhaseCsv(void);
static void USB_Send_FullReport(void);
static void USB_Send_GraphDataPack(void);
static void USB_Send_ReportScalarsFull(void);
static void USB_Send_Spectral_MetaAndModelIdx(const char *mode);
static void USB_Send_Spectral_Section_Esprit(void);
static void USB_Send_Spectral_Section_MusicEval(void);
static void USB_Send_Spectral_Section_FourierCsv(void);
static void USB_Send_Spectral_Section_MusicCsv(void);
static void USB_Send_Spectral_Section_Cyclic2Csv(void);
static void USB_Send_Spectral_Section_SkCsv(void);
static void USB_Send_Spectral_Section_WelchCsv(void);
static void USB_Send_Spectral_Section_CohCsv(void);
static void USB_Send_Spectral_Section_Wavelet(void);
static void USB_Send_Spectral_Section_PhaseOverlays(void);
static void USB_Send_CsvSpectrumRows(const char *line_tag, const float *bins);
static void USB_Send_CsvSpectrumRowsFromHalf(const char *line_tag, const float *half,
                                             uint32_t half_n, uint32_t nbins, uint8_t sqrt_psd);

static void USB_Process_TextLine(uint8_t *buf, uint32_t len)
{
  while (len > 0u && (buf[len - 1u] == '\r' || buf[len - 1u] == '\n')) len--;
  if (len == 0u) return;
  if (len >= sizeof(usb_cdc_tx_buf)) len = sizeof(usb_cdc_tx_buf) - 1u;
  buf[len] = '\0';

  char *line = (char *)buf;
  while (*line == ' ' || *line == '\t') line++;

  /* ---- Key=Value commands (with validation) ---- */
  char *eq = strchr(line, '=');
  if (eq != NULL)
  {
    char key[12];
    size_t kl = (size_t)(eq - line);
    if (kl >= sizeof(key)) return;
    memcpy(key, line, kl);
    key[kl] = '\0';
    for (size_t i = 0; key[i] != '\0'; i++)
      key[i] = (char)tolower((unsigned char)key[i]);

    const float v_raw = strtof(eq + 1, NULL);
    float v;

    if (strcmp(key, "rpm") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_RPM);
      if (Validate_RPM(v)) { g_params.motor_rpm = v; USB_Send_TextOkFloat("RPM", v, USB_DEC_RPM); }
      else USB_Send_TextLine("ERR RPM range (0..50000)\r\n");
      return;
    }
    if (strcmp(key, "slip") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_SLIP);
      if (Validate_Slip(v)) { g_params.motor_slip = v; USB_Send_TextOkFloat("SLIP", v, USB_DEC_SLIP); }
      else USB_Send_TextLine("ERR SLIP range (0..1)\r\n");
      return;
    }
    if (strcmp(key, "bpfo") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_COEFF);
      if (Validate_Coefficient(v)) {
        g_params.bpfo_coefficient = v;
        USB_Send_TextOkFloat("BPFO", v, USB_DEC_COEFF);
      } else USB_Send_TextLine("ERR BPFO range (0..100)\r\n");
      return;
    }
    if (strcmp(key, "bpfi") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_COEFF);
      if (Validate_Coefficient(v)) {
        g_params.bpfi_coefficient = v;
        USB_Send_TextOkFloat("BPFI", v, USB_DEC_COEFF);
      } else USB_Send_TextLine("ERR BPFI range (0..100)\r\n");
      return;
    }
    if (strcmp(key, "ftf") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_COEFF);
      if (Validate_Coefficient(v)) {
        g_params.ftf_coefficient = v;
        USB_Send_TextOkFloat("FTF", v, USB_DEC_COEFF);
      } else USB_Send_TextLine("ERR FTF range (0..100)\r\n");
      return;
    }
    if (strcmp(key, "bsf") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_COEFF);
      if (Validate_Coefficient(v)) {
        g_params.bsf_coefficient = v;
        USB_Send_TextOkFloat("BSF", v, USB_DEC_COEFF);
      } else USB_Send_TextLine("ERR BSF range (0..100)\r\n");
      return;
    }
    if (strcmp(key, "fth") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_FTH);
      if (Validate_Threshold(v)) { g_params.fault_threshold = v; USB_Send_TextOkFloat("FTH", v, USB_DEC_FTH); }
      else USB_Send_TextLine("ERR FTH range (0..1000)\r\n");
      return;
    }
    if (strcmp(key, "wth") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_WTH);
      if (Validate_Threshold(v)) { g_params.warning_threshold = v; USB_Send_TextOkFloat("WTH", v, USB_DEC_WTH); }
      else USB_Send_TextLine("ERR WTH range (0..1000)\r\n");
      return;
    }
    if (strcmp(key, "line") == 0) {
      v = USB_QuantizeDec(v_raw, USB_DEC_LINE);
      if (isfinite(v) && v > 1.0f && v < 400.0f) {
        g_supply_line_hz = v;
        USB_Send_TextOkFloat("LINE", v, USB_DEC_LINE);
      } else USB_Send_TextLine("ERR LINE range (1..400)\r\n");
      return;
    }
    if (strcmp(key, "slipauto") == 0) {
      const int on = (strtol(eq + 1, NULL, 10) != 0) ? 1 : 0;
      g_slip_auto_en = (uint8_t)on;
      USB_Send_TextLine(on ? "OK SLIPAUTO=1\r\n" : "OK SLIPAUTO=0\r\n");
      return;
    }
    if (strcmp(key, "stream") == 0) {
      const int on = (strtol(eq + 1, NULL, 10) != 0) ? 1 : 0;
      g_usb_binary_stream = (uint8_t)on;
      USB_Send_TextLine(on ? "OK STREAM=1\r\n" : "OK STREAM=0\r\n");
      return;
    }
    USB_Send_TextLine("ERR unknown key\r\n");
    return;
  }

  /* ---- Single-word commands ---- */
  char low[16];
  size_t wi = 0u;
  const char *p = line;
  while (*p != '\0' && *p != ' ' && *p != '\t' && wi < sizeof(low) - 1u)
  {
    low[wi++] = (char)tolower((unsigned char)*p);
    p++;
  }
  low[wi] = '\0';

  while (*p != '\0' && (*p == ' ' || *p == '\t')) {
    p++;
  }
  const char *args = (*p != '\0') ? p : NULL;

  if (strcmp(low, "date") == 0)
  {
    unsigned y = 0u, mo = 0u, d = 0u;
    if (args == NULL || sscanf(args, "%u-%u-%u", &y, &mo, &d) != 3) {
      USB_Send_TextLine("ERR DATE use yyyy-mm-dd\r\n");
      return;
    }
    if (App_Clock_SetDate((uint16_t)y, (uint8_t)mo, (uint8_t)d) != 0) {
      USB_Send_TextLine("ERR DATE invalid\r\n");
      return;
    }
    USB_Send_TextLine("OK\r\n");
    return;
  }
  if (strcmp(low, "time") == 0)
  {
    unsigned h = 0u, mi = 0u, s = 0u;
    int n;
    if (args == NULL) {
      USB_Send_TextLine("ERR TIME use hh:mm or hh:mm:ss\r\n");
      return;
    }
    n = sscanf(args, "%u:%u:%u", &h, &mi, &s);
    if (n == 2) {
      s = 0u;
    } else if (n != 3) {
      USB_Send_TextLine("ERR TIME use hh:mm or hh:mm:ss\r\n");
      return;
    }
    if (App_Clock_SetTime((uint8_t)h, (uint8_t)mi, (uint8_t)s) != 0) {
      USB_Send_TextLine("ERR TIME invalid\r\n");
      return;
    }
    USB_Send_TextLine("OK\r\n");
    return;
  }

  if (strcmp(low, "help") == 0 || strcmp(low, "?") == 0)
  {
    USB_Send_TextLine(
      "PING GET GETCOEFF REPORT FULLREPORT GRAPHDATA PHASE3 PHASECSV FFTCSV MUSICCSV ESPRITCSV CYCLIC2CSV SKCSV WELCHCSV COHCSV WAVELETCSV\r\n"
      "RPM= SLIP= SLIPAUTO=0/1 STREAM=0/1 BPFO= BPFI= BSF= FTF= FTH= WTH= LINE= (SET ack: OK KEY=value) DATE TIME SAVE LOAD DEFAULT SAVEADC\r\n"
      "CALIB SAVEBASE LOADBASE CLEARBASE CALIBST SAVESTST LOADSTST CLEARSTST\r\n"
      "STREAM=1=binary live frame (~260B, all models/quality); STREAM=0=text (default). CSV exports always text.\r\n");
    return;
  }
  if (strcmp(low, "ping") == 0)    { USB_Send_TextLine("OK\r\n"); return; }

  if (strcmp(low, "report") == 0) {
    USB_Send_FullReport();
    return;
  }
  if (strcmp(low, "fullreport") == 0) {
    USB_Send_FullReport();
    return;
  }
  if (strcmp(low, "graphdata") == 0 || strcmp(low, "graphs") == 0) {
    USB_Send_GraphDataPack();
    return;
  }
  if (strcmp(low, "phase3") == 0 || strcmp(low, "phasecsv") == 0) {
    USB_Send_PhaseCsv();
    return;
  }
  if (strcmp(low, "fftcsv") == 0) {
    USB_Send_Spectral_MetaAndModelIdx("FOURIER");
    USB_Send_Spectral_Section_FourierCsv();
    USB_Send_Spectral_Section_PhaseOverlays();
    USB_Send_TextLine("### END_EXPORT MODE=FOURIER\r\n");
    return;
  }
  if (strcmp(low, "musiccsv") == 0) {
    USB_Send_Spectral_MetaAndModelIdx("MUSIC");
    USB_Send_Spectral_Section_MusicEval();
    USB_Send_Spectral_Section_MusicCsv();
    USB_Send_Spectral_Section_PhaseOverlays();
    USB_Send_TextLine("### END_EXPORT MODE=MUSIC\r\n");
    return;
  }
  if (strcmp(low, "espritcsv") == 0) {
    USB_Send_Spectral_MetaAndModelIdx("ESPRIT");
    USB_Send_Spectral_Section_Esprit();
    USB_Send_TextLine("### END_EXPORT MODE=ESPRIT\r\n");
    return;
  }
  if (strcmp(low, "cyclic2csv") == 0) {
    USB_Send_Spectral_MetaAndModelIdx("CYCLIC2");
    USB_Send_Spectral_Section_Cyclic2Csv();
    USB_Send_TextLine("### END_EXPORT MODE=CYCLIC2\r\n");
    return;
  }
  if (strcmp(low, "skcsv") == 0) {
    USB_Send_Spectral_MetaAndModelIdx("SK");
    USB_Send_Spectral_Section_SkCsv();
    USB_Send_TextLine("### END_EXPORT MODE=SK\r\n");
    return;
  }
  if (strcmp(low, "welchcsv") == 0) {
    USB_Send_Spectral_MetaAndModelIdx("WELCH");
    USB_Send_Spectral_Section_WelchCsv();
    USB_Send_TextLine("### END_EXPORT MODE=WELCH\r\n");
    return;
  }
  if (strcmp(low, "cohcsv") == 0) {
    USB_Send_Spectral_MetaAndModelIdx("COH");
    USB_Send_Spectral_Section_CohCsv();
    USB_Send_TextLine("### END_EXPORT MODE=COH\r\n");
    return;
  }
  if (strcmp(low, "waveletcsv") == 0) {
    USB_Send_Spectral_MetaAndModelIdx("WAVELET");
    USB_Send_Spectral_Section_Wavelet();
    USB_Send_TextLine("### END_EXPORT MODE=WAVELET\r\n");
    return;
  }

  if (strcmp(low, "get") == 0 || strcmp(low, "getcoeff") == 0 || strcmp(low, "getcoeffs") == 0)
  {
    char *p   = (char *)usb_cdc_tx_buf;
    char *const pe = p + sizeof(usb_cdc_tx_buf);
    size_t room;
    int r;
#define U_SN(fmt, ...)                                                         \
    do {                                                                        \
      room = (pe > p) ? (size_t)(pe - p) : 0u;                                 \
      if (room < 2u) goto get_tx;                                               \
      r = snprintf(p, room, (fmt), __VA_ARGS__);                                \
      if (r < 0) goto get_tx;                                                   \
      { size_t w = (size_t)r; if (w >= room) w = room - 1u; p += w; }          \
    } while (0)
#define U_D(x, d)                                                              \
    do {                                                                        \
      room = (pe > p) ? (size_t)(pe - p) : 0u;                                 \
      if (room < 2u) goto get_tx;                                               \
      { size_t w = fmt_double(p, room, (double)(x), (unsigned)(d));             \
        if (w >= room) p = pe - 1; else p += w; }                              \
    } while (0)

    if (strcmp(low, "getcoeff") == 0 || strcmp(low, "getcoeffs") == 0)
    {
      U_SN("%s", "COEFF");
      U_SN("%s", " BPFO="); U_D(g_params.bpfo_coefficient, USB_DEC_COEFF);
      U_SN("%s", " BPFI="); U_D(g_params.bpfi_coefficient, USB_DEC_COEFF);
      U_SN("%s", " BSF=");  U_D(g_params.bsf_coefficient, USB_DEC_COEFF);
      U_SN("%s", " FTF=");  U_D(g_params.ftf_coefficient, USB_DEC_COEFF);
      U_SN("%s", "\r\n");
      goto get_tx;
    }

    U_SN("%s", "RPM=");   U_D(g_params.motor_rpm, USB_DEC_RPM);
    U_SN("%s", " SLIP="); U_D(g_params.motor_slip, USB_DEC_SLIP);
    U_SN("%s", " BPFO="); U_D(g_params.bpfo_coefficient, USB_DEC_COEFF);
    U_SN("%s", " BPFI="); U_D(g_params.bpfi_coefficient, USB_DEC_COEFF);
    U_SN("%s", " BSF=");  U_D(g_params.bsf_coefficient, USB_DEC_COEFF);
    U_SN("%s", " FTF=");  U_D(g_params.ftf_coefficient, USB_DEC_COEFF);
    U_SN("%s", " FTH=");  U_D(g_params.fault_threshold, USB_DEC_FTH);
    U_SN("%s", " WTH=");  U_D(g_params.warning_threshold, USB_DEC_WTH);
    U_SN("%s", " LINE="); U_D(g_supply_line_hz, USB_DEC_LINE);
    U_SN(" SLIPAUTO=%u", (unsigned)g_slip_auto_en);
    {
      uint16_t cy;
      uint8_t cmo, cd, ch, cmi, cs;
      App_Clock_Get(&cy, &cmo, &cd, &ch, &cmi, &cs);
      U_SN("%s", " DATE=");
      room = (pe > p) ? (size_t)(pe - p) : 0u;
      if (room >= 12u) {
        const int r2 = snprintf(p, room, "%04u-%02u-%02u",
                                (unsigned)cy, (unsigned)cmo, (unsigned)cd);
        if (r2 > 0) {
          size_t w2 = (size_t)r2;
          if (w2 >= room) {
            w2 = room - 1u;
          }
          p += w2;
        }
      }
      U_SN("%s", " TIME=");
      room = (pe > p) ? (size_t)(pe - p) : 0u;
      if (room >= 10u) {
        const int r3 = snprintf(p, room, "%02u:%02u:%02u",
                                (unsigned)ch, (unsigned)cmi, (unsigned)cs);
        if (r3 > 0) {
          size_t w3 = (size_t)r3;
          if (w3 >= room) {
            w3 = room - 1u;
          }
          p += w3;
        }
      }
    }
    U_SN("%s", "\r\n");
#undef U_D
#undef U_SN
get_tx:
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen((char *)usb_cdc_tx_buf));
    return;
  }

  if (strcmp(low, "save") == 0) {
    USB_Send_TextLine(Params_Save_To_QSPI() ? "OK SAVE\r\n" : "ERR SAVE\r\n");
    return;
  }
  if (strcmp(low, "load") == 0) {
    USB_Send_TextLine(Params_Load_From_QSPI() ? "OK LOAD\r\n" : "ERR LOAD\r\n");
    return;
  }
  if (strcmp(low, "default") == 0) {
    Params_Load_Defaults();
    USB_Send_TextLine("OK DEFAULT\r\n");
    return;
  }
  if (strcmp(low, "saveadc") == 0) {
    g_adc_save_requested = 1u;
    USB_Send_TextLine("OK SAVEADC (next capture -> SPI1 NOR)\r\n");
    return;
  }
  if (strcmp(low, "calib") == 0) {
    g_baseline_calib_active = 1u;
    g_baseline_calib_count = 0u;
    g_baseline_valid = 0u;
    memset(g_baseline_acc, 0, sizeof(g_baseline_acc));
    memset(g_baseline_acc2, 0, sizeof(g_baseline_acc2));
    USB_Send_TextLine("OK CALIB (run motor healthy ~26s)\r\n");
    return;
  }
  if (strcmp(low, "savebase") == 0) {
    USB_Send_TextLine(Baseline_Save_To_QSPI() ? "OK SAVEBASE\r\n" : "ERR SAVEBASE\r\n");
    return;
  }
  if (strcmp(low, "loadbase") == 0) {
    USB_Send_TextLine(Baseline_Load_From_QSPI() ? "OK LOADBASE\r\n" : "ERR LOADBASE\r\n");
    return;
  }
  if (strcmp(low, "clearbase") == 0) {
    g_baseline_valid = 0u;
    g_baseline_calib_active = 0u;
    g_baseline_calib_count = 0u;
    g_cusum_pos = 0.f;
    g_fault.cusum_score = 0.f;
    USB_Send_TextLine("OK CLEARBASE\r\n");
    return;
  }
  if (strcmp(low, "calibst") == 0) {
    g_stator_calib_active = 1u;
    g_stator_calib_count = 0u;
    g_stator_baseline_valid = 0u;
    memset(g_stator_baseline_acc, 0, sizeof(g_stator_baseline_acc));
    memset(g_stator_baseline_acc2, 0, sizeof(g_stator_baseline_acc2));
    USB_Send_TextLine("OK CALIBST (healthy stator ~26s)\r\n");
    return;
  }
  if (strcmp(low, "savestst") == 0) {
    USB_Send_TextLine(StatorBaseline_Save_To_QSPI() ? "OK SAVESTST\r\n" : "ERR SAVESTST\r\n");
    return;
  }
  if (strcmp(low, "loadstst") == 0) {
    USB_Send_TextLine(StatorBaseline_Load_From_QSPI() ? "OK LOADSTST\r\n" : "ERR LOADSTST\r\n");
    return;
  }
  if (strcmp(low, "clearstst") == 0) {
    g_stator_baseline_valid = 0u;
    g_stator_calib_active = 0u;
    g_stator_calib_count = 0u;
    g_stator_cusum_short = 0.f;
    g_stator_cusum_gnd = 0.f;
    g_fault.stator_cusum_short = 0.f;
    g_fault.stator_cusum_gnd = 0.f;
    USB_Send_TextLine("OK CLEARSTST\r\n");
    return;
  }

  USB_Send_TextLine("ERR unknown command (HELP)\r\n");
}

static void USB_Process_Received_Data(void)
{
  if (USB_LooksLikeText(g_usb_rx_buf, g_usb_rx_len))
  {
    USB_Process_TextLine(g_usb_rx_buf, g_usb_rx_len);
    return;
  }

  uint8_t cmd    = g_usb_rx_buf[0];
  uint8_t status = 0u;

  switch (cmd)
  {
    case CMD_PING:
      status = 1u;
      break;

    /* ---- All SET commands now validate before accepting ---- */
    case CMD_SET_RPM:
      if (g_usb_rx_len >= 5u) {
        float v = USB_QuantizeDec(usb_read_f32_le(&g_usb_rx_buf[1]), USB_DEC_RPM);
        if (Validate_RPM(v)) { g_params.motor_rpm = v; status = 1u; }
      }
      break;

    case CMD_SET_SLIP:
      if (g_usb_rx_len >= 5u) {
        float v = USB_QuantizeDec(usb_read_f32_le(&g_usb_rx_buf[1]), USB_DEC_SLIP);
        if (Validate_Slip(v)) { g_params.motor_slip = v; status = 1u; }
      }
      break;

    case CMD_SET_BPFO:
      if (g_usb_rx_len >= 5u) {
        float v = USB_QuantizeDec(usb_read_f32_le(&g_usb_rx_buf[1]), USB_DEC_COEFF);
        if (Validate_Coefficient(v)) { g_params.bpfo_coefficient = v; status = 1u; }
      }
      break;

    case CMD_SET_BPFI:
      if (g_usb_rx_len >= 5u) {
        float v = USB_QuantizeDec(usb_read_f32_le(&g_usb_rx_buf[1]), USB_DEC_COEFF);
        if (Validate_Coefficient(v)) { g_params.bpfi_coefficient = v; status = 1u; }
      }
      break;

    case CMD_SET_FTF:
      if (g_usb_rx_len >= 5u) {
        float v = USB_QuantizeDec(usb_read_f32_le(&g_usb_rx_buf[1]), USB_DEC_COEFF);
        if (Validate_Coefficient(v)) { g_params.ftf_coefficient = v; status = 1u; }
      }
      break;

    case CMD_SET_BSF:
      if (g_usb_rx_len >= 5u) {
        float v = USB_QuantizeDec(usb_read_f32_le(&g_usb_rx_buf[1]), USB_DEC_COEFF);
        if (Validate_Coefficient(v)) { g_params.bsf_coefficient = v; status = 1u; }
      }
      break;

    case CMD_SET_FAULT_THRESH:
      if (g_usb_rx_len >= 5u) {
        float v = USB_QuantizeDec(usb_read_f32_le(&g_usb_rx_buf[1]), USB_DEC_FTH);
        if (Validate_Threshold(v)) { g_params.fault_threshold = v; status = 1u; }
      }
      break;

    case CMD_SET_WARNING_THRESH:
      if (g_usb_rx_len >= 5u) {
        float v = USB_QuantizeDec(usb_read_f32_le(&g_usb_rx_buf[1]), USB_DEC_WTH);
        if (Validate_Threshold(v)) { g_params.warning_threshold = v; status = 1u; }
      }
      break;

    case CMD_SET_STREAM:
      if (g_usb_rx_len >= 2u) {
        g_usb_binary_stream = (g_usb_rx_buf[1] != 0u) ? 1u : 0u;
        status = 1u;
      }
      break;

    case CMD_GET_ALL_PARAMS:
      status = 1u;
      USB_Send_AllParams();
      break;

    case CMD_SAVE_TO_FLASH:
      status = Params_Save_To_QSPI();
      break;

    case CMD_LOAD_FROM_FLASH:
      status = Params_Load_From_QSPI();
      break;

    case CMD_RESET_TO_DEFAULT:
      Params_Load_Defaults();
      status = 1u;
      break;

    case CMD_SAVE_ADC_TO_SPI1:
      g_adc_save_requested = 1u;
      status = 1u;
      break;

    default:
      status = 0u;
      break;
  }

  if (cmd != CMD_GET_ALL_PARAMS)
  {
    USB_Send_Ack(cmd, status);
  }
}

/* Process any pending USB command — safe to call during DSP.
 * Without this, PING and other commands are blocked for the entire
 * DSP frame (5-10+ seconds with all detection methods on 8192 samples),
 * causing the PC application to timeout. */
static void USB_ServiceIfPending(void)
{
  if (g_usb_rx_ready)
  {
    g_usb_rx_ready = 0u;
    USB_Process_Received_Data();
  }
}

void USB_Rx_Callback(uint8_t *buf, uint32_t len)
{
  if (len == 0u) return;
  if (len > USB_RX_MAX_BYTES) len = USB_RX_MAX_BYTES;
  g_usb_rx_len = len;
  memcpy((void*)g_usb_rx_buf, (const void*)buf, len);
  g_usb_rx_ready = 1u;
}

/* ============================================================================
 * Signal processing buffers
 * ============================================================================
 */

static float phase_a[FP_SAMPLES_PER_PHASE];
static float phase_b[FP_SAMPLES_PER_PHASE];
static float phase_c[FP_SAMPLES_PER_PHASE];
static float window_hamming[FP_SAMPLES_PER_PHASE];
static float sub_R[(FP_SUB_L) * (FP_SUB_L)];
static float sub_A[(FP_SUB_L) * (FP_SUB_L)];
static float sub_V[(FP_SUB_L) * (FP_SUB_L)];
static float sig_res_mean[FP_SAMPLES_PER_PHASE];
static float teager_buf[FP_SAMPLES_PER_PHASE];
static float kurt_filt_buf[FP_SAMPLES_PER_PHASE];
static float kurt_env_buf[FP_SAMPLES_PER_PHASE];
static float fft_re[FP_KURT_FFT_N];
static float fft_im[FP_KURT_FFT_N];
static float kb_b0[FP_KURT_NBANDS];
static float kb_b1[FP_KURT_NBANDS];
static float kb_b2[FP_KURT_NBANDS];
static float kb_a1[FP_KURT_NBANDS];
static float kb_a2[FP_KURT_NBANDS];
static uint16_t kb_fc_hz[FP_KURT_NBANDS];
static uint8_t g_kurt_coeffs_ready = 0u;
static float RAM_D2_BULK wav_buf_a[FP_FFT_SIZE];
static float RAM_D2_BULK wav_buf_b[FP_FFT_SIZE];
/* Welch cross-spectrum segment scratch (32 KiB) — in D1 .bss; RAM_D2 is full at 8192-pt build. */
static float welch_xa_re[FP_HALF_FFT];
static float welch_xa_im[FP_HALF_FFT];

static volatile uint8_t g_adc1_done = 0;
static volatile uint8_t g_adc2_done = 0;
static volatile uint8_t g_adc3_done = 0;
static volatile uint8_t g_all_ready = 0;

/* ============================================================================
 * Core DSP functions
 * ============================================================================
 */

static float compute_shaft_frequency_hz(void)
{
  return (g_params.motor_rpm * (1.0f - g_params.motor_slip)) / 60.0f;
}

static void Window_Generate_Hamming(void)
{
  const float denom = (float)(FP_SAMPLES_PER_PHASE - 1u);
  for (uint32_t i = 0; i < FP_SAMPLES_PER_PHASE; i++)
    window_hamming[i] = 0.54f - 0.46f * cosf(2.0f * 3.14159265358979323846f * (float)i / denom);
}

static void Remove_DC(float *sig, uint32_t n)
{
  if (n == 0u) return;
  double sum = 0.0;
  for (uint32_t i = 0; i < n; i++) sum += (double)sig[i];
  const float mean = (float)(sum / (double)n);
  for (uint32_t i = 0; i < n; i++) sig[i] -= mean;
}

static void Bearing_CalcFaultFundamentalsAt(float fr_hz, float *bpfo, float *bpfi, float *bsf, float *ftf)
{
  *bpfo = g_params.bpfo_coefficient * fr_hz;
  *bpfi = g_params.bpfi_coefficient * fr_hz;
  *ftf  = g_params.ftf_coefficient  * fr_hz;
  *bsf  = g_params.bsf_coefficient  * fr_hz;
}

/* ============================================================================
 * Neyman-Pearson: F distribution (incomplete beta)
 * ============================================================================
 */

static float betacf(float a, float b, float x)
{
  const int MAXIT = 100;
  const double EPS = 3e-7;
  const double FPMIN = 1e-30;
  double qab = (double)(a + b);
  double qap = (double)(a + 1.0f);
  double qam = (double)(a - 1.0f);
  double c = 1.0, d = 1.0 - (qab * (double)x / qap);
  if (fabs(d) < FPMIN) d = FPMIN;
  d = 1.0 / d;
  double h = d;
  for (int m = 1; m <= MAXIT; m++)
  {
    int m2 = 2 * m;
    double aa = (double)m * (b - (double)m) * (double)x /
                ((qam + (double)m2) * (a + (double)m2));
    d = 1.0 + aa * d; if (fabs(d) < FPMIN) d = FPMIN;
    c = 1.0 + aa / c; if (fabs(c) < FPMIN) c = FPMIN;
    d = 1.0 / d; h *= d * c;
    aa = -(a + (double)m) * (qab + (double)m) * (double)x /
         ((a + (double)m2) * (qap + (double)m2));
    d = 1.0 + aa * d; if (fabs(d) < FPMIN) d = FPMIN;
    c = 1.0 + aa / c; if (fabs(c) < FPMIN) c = FPMIN;
    d = 1.0 / d;
    double del = d * c; h *= del;
    if (fabs(del - 1.0) < EPS) break;
  }
  return (float)h;
}

static float betai(float a, float b, float x)
{
  if (x <= 0.0f) return 0.0f;
  if (x >= 1.0f) return 1.0f;
  double lbeta = lgamma(a + b) - lgamma(a) - lgamma(b);
  double bt = exp(lbeta + (double)a * log((double)x) + (double)b * log(1.0 - (double)x));
  if (x < (a + 1.0f) / (a + b + 2.0f))
    return (float)(bt * betacf(a, b, x) / (double)a);
  else
    return (float)(1.0 - bt * betacf(b, a, 1.0f - x) / (double)b);
}

static float fdist_cdf(float x, float d1, float d2)
{
  if (x <= 0.0f) return 0.0f;
  double xx = ((double)d1 * (double)x) / ((double)d1 * (double)x + (double)d2);
  return betai(d1 / 2.0f, d2 / 2.0f, (float)xx);
}

static float fdist_inv_cdf(float p, float d1, float d2)
{
  float lo = 0.0f, hi = 1.0f;
  for (int i = 0; i < 32; i++) { if (fdist_cdf(hi, d1, d2) >= p) break; hi *= 2.0f; }
  for (int it = 0; it < 40; it++) {
    float mid = 0.5f * (lo + hi);
    if (fdist_cdf(mid, d1, d2) < p) lo = mid; else hi = mid;
  }
  return hi;
}

/* Neyman-Pearson critical ratio: reject H0 (healthy) if measured ratio > gamma (fixed Pfa). */
static float NP_GammaRatio(uint32_t n_fault, uint32_t n_base, float pfa)
{
  if (n_fault == 0u || n_base < 2u) {
    return 1.0e30f;
  }
  const float d1 = 2.0f * (float)n_fault;
  const float d2 = 2.0f * (float)n_base;
  return fdist_inv_cdf(1.0f - pfa, d1, d2);
}

static uint8_t NP_TestAmplitudeRatio(float ratio, uint32_t n_fault, uint32_t n_base,
                                     float pfa, float *gamma_out)
{
  const float g = NP_GammaRatio(n_fault, n_base, pfa);
  if (gamma_out != NULL) {
    *gamma_out = g;
  }
  if (n_fault == 0u || n_base < 2u) {
    return 0u;
  }
  return (ratio > g) ? 1u : 0u;
}

/* ============================================================================
 * LS sinusoid fitting + Fortescue + Teager-Kaiser
 * ============================================================================
 */

/* Single-tone least-squares normal-equation sums for x against cos(wn)/sin(wn).
 * Uses a double-precision phasor recurrence (c+js)_{n+1} = (c+js)_n*(cos w + j sin w)
 * instead of cosf/sinf(w*n): for N up to 8192 the float32 argument w*n reaches
 * ~1e4 rad where the libm trig loses ~1e-3 rad of phase (biasing the fit at higher
 * harmonics). The recurrence keeps phase exact to ~N*eps_double (~1e-12) and drops
 * the per-sample transcendental cost. H7 has a hardware double-precision FPU. */
static void LS_NormalEqs(const float *x, uint32_t N, double w,
                         double *o_scc, double *o_scs, double *o_sss,
                         double *o_sxc, double *o_sxs)
{
  const double cw = cos(w), sw = sin(w);
  double cn = 1.0, sn = 0.0;
  double scc = 0.0, scs = 0.0, sss = 0.0, sxc = 0.0, sxs = 0.0;
  for (uint32_t n = 0; n < N; n++) {
    const double xv = (double)x[n];
    scc += cn * cn; scs += cn * sn; sss += sn * sn;
    sxc += xv * cn; sxs += xv * sn;
    const double cn1 = cn * cw - sn * sw;
    sn = sn * cw + cn * sw;
    cn = cn1;
  }
  *o_scc = scc; *o_scs = scs; *o_sss = sss; *o_sxc = sxc; *o_sxs = sxs;
}

static void LS_Sinusoid_AB(const float *x, uint32_t N, float f_hz, float fs,
                            float *out_a, float *out_b)
{
  const double w = 2.0 * 3.14159265358979323846 * (double)f_hz / (double)fs;
  double scc, scs, sss, sxc, sxs;
  LS_NormalEqs(x, N, w, &scc, &scs, &sss, &sxc, &sxs);
  const double det = scc * sss - scs * scs;
  if (fabs(det) < 1e-24) { *out_a = 0.f; *out_b = 0.f; return; }
  *out_a = (float)((sxc * sss - sxs * scs) / det);
  *out_b = (float)((scc * sxs - scs * sxc) / det);
}

static float LS_Sinusoid_Power(const float *x, uint32_t N, float f_hz, float fs)
{
  float A, B;
  LS_Sinusoid_AB(x, N, f_hz, fs, &A, &B);
  return A * A + B * B;
}

static float LS_Power_Max3Ph_AtF(float f_hz)
{
  const float pa = LS_Sinusoid_Power(phase_a, FP_FFT_SIZE, f_hz, FP_SAMPLE_RATE_HZ);
  const float pb = LS_Sinusoid_Power(phase_b, FP_FFT_SIZE, f_hz, FP_SAMPLE_RATE_HZ);
  const float pc = LS_Sinusoid_Power(phase_c, FP_FFT_SIZE, f_hz, FP_SAMPLE_RATE_HZ);
  return fmaxf(fmaxf(pa, pb), pc);
}

static float LS_Refined_Max_Max3Ph(float f_center, float half_bw_hz, uint32_t steps)
{
  if (steps < 2u) return LS_Power_Max3Ph_AtF(f_center);
  float lo = f_center - half_bw_hz; if (lo < 0.f) lo = 0.f;
  float hi = f_center + half_bw_hz;
  if (hi > 0.45f * FP_SAMPLE_RATE_HZ) hi = 0.45f * FP_SAMPLE_RATE_HZ;
  float mx = 0.f;
  const float denom = (float)(steps - 1u);
  for (uint32_t i = 0; i < steps; i++) {
    const float f = lo + (hi - lo) * (float)i / denom;
    const float p = LS_Power_Max3Ph_AtF(f);
    if (p > mx) mx = p;
  }
  return mx;
}

static float Sideband_Power_Max3Ph(float f_line_hz, float f_fault_hz)
{
  const float f_lo = fabsf(f_line_hz - f_fault_hz);
  const float f_hi = f_line_hz + f_fault_hz;
  const float p_lo = LS_Refined_Max_Max3Ph(f_lo, g_params.bandwidth_hz * 0.5f, 11u);
  const float p_hi = LS_Refined_Max_Max3Ph(f_hi, g_params.bandwidth_hz * 0.5f, 11u);
  return fmaxf(p_lo, p_hi);
}

static float Envelope_Acf_PeakAtF(const float *env, uint32_t N, float f_fault_hz, float fs)
{
  if (f_fault_hz < 2.f || N < 64u) return 0.f;
  const uint32_t lag0 = (uint32_t)(fs / f_fault_hz + 0.5f);
  if (lag0 < 2u || lag0 >= N / 2u) return 0.f;

  double e0 = 0.0;
  for (uint32_t n = 0u; n < N; n++) {
    const double v = (double)env[n];
    e0 += v * v;
  }
  if (e0 < 1e-30) return 0.f;

  float peak = 0.f;
  for (int32_t d = -3; d <= 3; d++) {
    const int64_t lag = (int64_t)lag0 + (int64_t)d;
    if (lag < 1 || lag >= (int64_t)(N / 2u)) continue;
    double acc = 0.0;
    const uint32_t M = N - (uint32_t)lag;
    for (uint32_t n = 0u; n < M; n++)
      acc += (double)env[n] * (double)env[n + (uint32_t)lag];
    const float acf = (float)(acc / e0);
    if (acf > peak) peak = acf;
  }
  return peak;
}

static float FusePerFaultScore(float ls_r, float sb_r, float acf_r, float cy_r,
                               float mu_r, float ep_r, uint8_t music_ok, uint8_t esprit_ok)
{
  float prod = fmaxf(ls_r * sb_r * acf_r * cy_r, 0.f);
  float n_f = 4.f;
  if (music_ok) { prod *= mu_r; n_f += 1.f; }
  if (esprit_ok) { prod *= ep_r; n_f += 1.f; }
  return powf(prod, 1.f / n_f);
}

static float WeightedGlobalFusion(float r_ls, float r_mu, float r_ep, float r_tg,
                                  float r_sk, float r_wav, float r_cyc,
                                  float r_sb, float r_acf, float r_welch, float r_coh,
                                  uint8_t music_ok, uint8_t esprit_ok)
{
  const float w_ls = 1.20f, w_mu = 1.10f, w_ep = 1.10f, w_tg = 0.90f;
  const float w_sk = 1.00f, w_wv = 0.70f, w_cy = 1.15f, w_sb = 1.20f, w_acf = 1.10f;
  const float w_wel = 1.05f, w_coh = 1.10f;
  double log_sum = 0.0;
  double w_sum = 0.0;
  log_sum += (double)w_ls * log((double)fmaxf(r_ls, 1e-12f)); w_sum += w_ls;
  log_sum += (double)w_tg * log((double)fmaxf(r_tg, 1e-12f)); w_sum += w_tg;
  log_sum += (double)w_sk * log((double)fmaxf(r_sk, 1e-12f)); w_sum += w_sk;
  log_sum += (double)w_wv * log((double)fmaxf(r_wav, 1e-12f)); w_sum += w_wv;
  log_sum += (double)w_cy * log((double)fmaxf(r_cyc, 1e-12f)); w_sum += w_cy;
  log_sum += (double)w_sb * log((double)fmaxf(r_sb, 1e-12f)); w_sum += w_sb;
  log_sum += (double)w_acf * log((double)fmaxf(r_acf, 1e-12f)); w_sum += w_acf;
  log_sum += (double)w_wel * log((double)fmaxf(r_welch, 1e-12f)); w_sum += w_wel;
  log_sum += (double)w_coh * log((double)fmaxf(r_coh, 1e-12f)); w_sum += w_coh;
  if (music_ok) { log_sum += (double)w_mu * log((double)fmaxf(r_mu, 1e-12f)); w_sum += w_mu; }
  if (esprit_ok) { log_sum += (double)w_ep * log((double)fmaxf(r_ep, 1e-12f)); w_sum += w_ep; }
  return (float)exp(log_sum / w_sum);
}

static void ADC_TryAutoCalibrateOffsets(void)
{
  if (g_adc_autocal_done) return;

  const double inv_fs = 1.0 / (double)FP_ADC_RESOLUTION_F;
  const double vref = (double)FP_VREF;
  const double sens = (double)FP_CURRENT_SENSITIVITY;
  double sa = 0.0, sb = 0.0, sc = 0.0;
  const double invn = 1.0 / (double)FP_SAMPLES_PER_PHASE;
  for (uint32_t i = 0u; i < FP_SAMPLES_PER_PHASE; i++) {
    sa += (double)raw_adc1[i] * inv_fs * vref;
    sb += (double)raw_adc2[i] * inv_fs * vref;
    sc += (double)raw_adc3[i] * inv_fs * vref;
  }
  const double ma = sa * invn;
  const double mb = sb * invn;
  const double mc = sc * invn;

  double ra = 0.0, rb = 0.0, rc = 0.0;
  for (uint32_t i = 0u; i < FP_SAMPLES_PER_PHASE; i++) {
    const double va = (double)raw_adc1[i] * inv_fs * vref - ma;
    const double vb = (double)raw_adc2[i] * inv_fs * vref - mb;
    const double vc = (double)raw_adc3[i] * inv_fs * vref - mc;
    ra += va * va; rb += vb * vb; rc += vc * vc;
  }
  const float ia_rms = (float)(sqrt(ra * invn) / sens);
  const float ib_rms = (float)(sqrt(rb * invn) / sens);
  const float ic_rms = (float)(sqrt(rc * invn) / sens);
  if (ia_rms > FP_ADC_AUTOCAL_MAX_RMS_A ||
      ib_rms > FP_ADC_AUTOCAL_MAX_RMS_A ||
      ic_rms > FP_ADC_AUTOCAL_MAX_RMS_A)
    return;

  g_adc_offset_v[0] = (float)ma;
  g_adc_offset_v[1] = (float)mb;
  g_adc_offset_v[2] = (float)mc;
  g_adc_autocal_done = 1u;
}

static void Update_FaultIndex_Ema_Cusum(void)
{
  const float alpha = FP_FAULT_INDEX_EMA_ALPHA;
  if (g_fault.fault_index_ema <= 0.f)
    g_fault.fault_index_ema = g_fault.fault_index;
  else
    g_fault.fault_index_ema = (1.f - alpha) * g_fault.fault_index_ema + alpha * g_fault.fault_index;

  const float x = g_fault.fault_index_ema - 1.f - FP_CUSUM_DRIFT;
  g_cusum_pos = fmaxf(g_cusum_pos + x, 0.f);
  g_fault.cusum_score = g_cusum_pos;
}

static uint8_t PickDominantFault(const float pf[4])
{
  uint8_t best = 0u;
  float mx = 0.f;
  for (uint8_t i = 0u; i < 4u; i++) {
    if (pf[i] > mx) { mx = pf[i]; best = (uint8_t)(i + 1u); }
  }
  return best;
}

static void Fortescue_LS_MagAtF(float f_hz, float *m0, float *m1, float *m2)
{
  float Aa, Ba, Ab, Bb, Ac, Bc;
  LS_Sinusoid_AB(phase_a, FP_FFT_SIZE, f_hz, FP_SAMPLE_RATE_HZ, &Aa, &Ba);
  LS_Sinusoid_AB(phase_b, FP_FFT_SIZE, f_hz, FP_SAMPLE_RATE_HZ, &Ab, &Bb);
  LS_Sinusoid_AB(phase_c, FP_FFT_SIZE, f_hz, FP_SAMPLE_RATE_HZ, &Ac, &Bc);

  const float a_r = -0.5f, a_i = 0.86602540378443864676f;
  const float a2_r = -0.5f, a2_i = -a_i;

  const float I0r = (Aa + Ab + Ac) / 3.0f;
  const float I0i = (Ba + Bb + Bc) / 3.0f;

  const float u1r = a_r * Ab - a_i * Bb, u1i = a_r * Bb + a_i * Ab;
  const float u2r = a2_r * Ac - a2_i * Bc, u2i = a2_r * Bc + a2_i * Ac;
  const float I1r = (Aa + u1r + u2r) / 3.0f;
  const float I1i = (Ba + u1i + u2i) / 3.0f;

  const float v1r = a2_r * Ab - a2_i * Bb, v1i = a2_r * Bb + a2_i * Ab;
  const float v2r = a_r * Ac - a_i * Bc, v2i = a_r * Bc + a_i * Ac;
  const float I2r = (Aa + v1r + v2r) / 3.0f;
  const float I2i = (Ba + v1i + v2i) / 3.0f;

  *m0 = hypotf(I0r, I0i);
  *m1 = hypotf(I1r, I1i);
  *m2 = hypotf(I2r, I2i);
}

static float Phase_Currents_MeanRms(void)
{
  double sa = 0.0, sb = 0.0, sc = 0.0;
  const double invn = 1.0 / (double)FP_FFT_SIZE;
  for (uint32_t n = 0u; n < FP_FFT_SIZE; n++) {
    sa += (double)phase_a[n] * (double)phase_a[n];
    sb += (double)phase_b[n] * (double)phase_b[n];
    sc += (double)phase_c[n] * (double)phase_c[n];
  }
  return ((float)sqrt(sa * invn) + (float)sqrt(sb * invn) + (float)sqrt(sc * invn)) / 3.0f;
}

static float Residual_I0_TimeRms(void)
{
  double s = 0.0;
  const double invn = 1.0 / (double)FP_FFT_SIZE;
  for (uint32_t n = 0u; n < FP_FFT_SIZE; n++) {
    const double t = ((double)phase_a[n] + (double)phase_b[n] + (double)phase_c[n]) / 3.0;
    s += t * t;
  }
  return (float)sqrt(s * invn);
}

static void Stator_TimeDomain_SequenceRms(float *i0_rms, float *i1_rms, float *i2_rms)
{
  const float a_r = -0.5f;
  const float a_i = 0.86602540378443864676f;
  const float a2_r = -0.5f;
  const float a2_i = -a_i;
  double s0 = 0.0, s1 = 0.0, s2 = 0.0;
  const double invn = 1.0 / (double)FP_FFT_SIZE;

  for (uint32_t n = 0u; n < FP_FFT_SIZE; n++) {
    const float ia = phase_a[n];
    const float ib = phase_b[n];
    const float ic = phase_c[n];
    const double i0 = ((double)ia + (double)ib + (double)ic) / 3.0;
    const double i1r = ((double)ia + (double)a_r * (double)ib + (double)a2_r * (double)ic) / 3.0;
    const double i1i = ((double)a_i * (double)ib + (double)a2_i * (double)ic) / 3.0;
    const double i2r = ((double)ia + (double)a2_r * (double)ib + (double)a_r * (double)ic) / 3.0;
    const double i2i = ((double)a2_i * (double)ib + (double)a_i * (double)ic) / 3.0;
    s0 += i0 * i0;
    s1 += i1r * i1r + i1i * i1i;
    s2 += i2r * i2r + i2i * i2i;
  }
  *i0_rms = (float)sqrt(s0 * invn);
  *i1_rms = (float)sqrt(s1 * invn);
  *i2_rms = (float)sqrt(s2 * invn);
}

static float Stator_PhaseSpreadDeg(float f_hz, float fs)
{
  float Aa, Ba, Ab, Bb, Ac, Bc;
  LS_Sinusoid_AB(phase_a, FP_FFT_SIZE, f_hz, fs, &Aa, &Ba);
  LS_Sinusoid_AB(phase_b, FP_FFT_SIZE, f_hz, fs, &Ab, &Bb);
  LS_Sinusoid_AB(phase_c, FP_FFT_SIZE, f_hz, fs, &Ac, &Bc);
  const float pa = atan2f(Ba, Aa) * 57.29577951308232f;
  const float pb = atan2f(Bb, Ab) * 57.29577951308232f;
  const float pc = atan2f(Bc, Ac) * 57.29577951308232f;
  const float d1 = fabsf(pa - pb);
  const float d2 = fabsf(pb - pc);
  const float d3 = fabsf(pc - pa);
  return fmaxf(fmaxf(d1, d2), d3);
}

static float Stator_OddHarmIndex(float f_supply_hz, float fs, float fmax)
{
  const float P1 = LS_Power_Max3Ph_AtF(f_supply_hz);
  float odd = 0.f;
  for (uint32_t k = 3u; k <= 9u; k += 2u) {
    const float fh = f_supply_hz * (float)k;
    if (fh > fmax) break;
    odd += LS_Power_Max3Ph_AtF(fh);
  }
  return (P1 > 1e-24f) ? (odd / P1) : 0.f;
}

static float Stator_FuseShortIndex(float nsr, float nsr_td, float nsr_h5,
                                   float imb, float harm, float odd, float spread)
{
  const float r_nsr  = fmaxf(nsr / FP_STATOR_NSR_EARLY, 0.01f);
  const float r_ntd  = fmaxf(nsr_td / FP_STATOR_NSR_TD_EARLY, 0.01f);
  const float r_h5   = fmaxf(nsr_h5 / FP_STATOR_NSR_EARLY, 0.01f);
  const float r_imb  = fmaxf(imb / FP_STATOR_IMB_EARLY_PCT, 0.01f);
  const float r_harm = fmaxf(harm / FP_STATOR_HARM_EARLY, 0.01f);
  const float r_odd  = fmaxf(odd / FP_STATOR_ODD_HARM_EARLY, 0.01f);
  const float r_spr  = fmaxf(spread / FP_STATOR_PHASE_SPREAD_EARLY, 0.01f);
  return powf(r_nsr * r_ntd * r_h5 * r_imb * r_harm * r_odd * r_spr, 1.f / 7.f);
}

static float Stator_FuseGndIndex(float zsr, float zsr_td, float resid, float zsr_h3)
{
  const float r_zsr   = fmaxf(zsr / FP_STATOR_ZSR_EARLY, 0.01f);
  const float r_ztd   = fmaxf(zsr_td / FP_STATOR_ZSR_TD_EARLY, 0.01f);
  const float r_res   = fmaxf(resid / FP_STATOR_RESID_EARLY, 0.01f);
  const float r_h3    = fmaxf(zsr_h3 / FP_STATOR_ZSR_H3_EARLY, 0.01f);
  return powf(r_zsr * r_ztd * r_res * r_h3, 1.f / 4.f);
}

static void Stator_UpdateEmaCusum(void)
{
  const float alpha = FP_STATOR_INDEX_EMA_ALPHA;
  if (g_fault.stator_short_index_ema <= 0.f)
    g_fault.stator_short_index_ema = g_fault.stator_short_index;
  else
    g_fault.stator_short_index_ema =
        (1.f - alpha) * g_fault.stator_short_index_ema + alpha * g_fault.stator_short_index;

  if (g_fault.stator_gnd_index_ema <= 0.f)
    g_fault.stator_gnd_index_ema = g_fault.stator_gnd_index;
  else
    g_fault.stator_gnd_index_ema =
        (1.f - alpha) * g_fault.stator_gnd_index_ema + alpha * g_fault.stator_gnd_index;

  const float xs = g_fault.stator_short_index_ema - 1.f - FP_STATOR_CUSUM_DRIFT;
  const float xg = g_fault.stator_gnd_index_ema - 1.f - FP_STATOR_CUSUM_DRIFT;
  g_stator_cusum_short = fmaxf(g_stator_cusum_short + xs, 0.f);
  g_stator_cusum_gnd   = fmaxf(g_stator_cusum_gnd + xg, 0.f);
  g_fault.stator_cusum_short = g_stator_cusum_short;
  g_fault.stator_cusum_gnd   = g_stator_cusum_gnd;
}

/* ============================================================================
 * Stator Winding Analysis
 * Incipient short: NSR/NSR_TD/odd harmonics/phase spread + CUSUM (before hard short)
 * Incipient ground: ZSR/ZSR_TD/residual I0 + CUSUM
 * ============================================================================
 */

static void Stator_Winding_Analyze(float f_supply_hz)
{
  const float fs = FP_SAMPLE_RATE_HZ;
  const float fmax = 0.45f * fs;
  const float eps = 1e-9f;

  float m0, m1, m2;
  Fortescue_LS_MagAtF(f_supply_hz, &m0, &m1, &m2);
  const float m1d = fmaxf(m1, eps);

  float Aa, Ba, Ab, Bb, Ac, Bc;
  LS_Sinusoid_AB(phase_a, FP_FFT_SIZE, f_supply_hz, fs, &Aa, &Ba);
  LS_Sinusoid_AB(phase_b, FP_FFT_SIZE, f_supply_hz, fs, &Ab, &Bb);
  LS_Sinusoid_AB(phase_c, FP_FFT_SIZE, f_supply_hz, fs, &Ac, &Bc);
  const float ma = hypotf(Aa, Ba), mb = hypotf(Ab, Bb), mc = hypotf(Ac, Bc);
  const float mmin = fminf(fminf(ma, mb), mc);
  const float mmax = fmaxf(fmaxf(ma, mb), mc);
  const float mmean = (ma + mb + mc) / 3.0f;
  float imb_pct = 0.f;
  if (mmean > 1e-12f) imb_pct = 100.0f * (mmax - mmin) / mmean;

  const float P1 = LS_Power_Max3Ph_AtF(f_supply_hz);
  float harm_num = 0.f;
  for (uint32_t k = 3u; k <= 7u; k += 2u) {
    const float fh = f_supply_hz * (float)k;
    if (fh > fmax) break;
    harm_num += LS_Power_Max3Ph_AtF(fh);
  }
  const float harm_ratio = (P1 > 1e-24f) ? (harm_num / P1) : 0.f;

  const float mean_rms = Phase_Currents_MeanRms();
  const float i0_t = Residual_I0_TimeRms();
  const float resid_ratio = (mean_rms > 1e-12f) ? (i0_t / mean_rms) : 0.f;

  float zsr_h3 = 0.f;
  const float f3 = f_supply_hz * 3.0f;
  if (f3 <= fmax) {
    float h0, h1, h2;
    Fortescue_LS_MagAtF(f3, &h0, &h1, &h2);
    zsr_h3 = h0 / fmaxf(h1, eps);
  }

  float i0_rms, i1_rms, i2_rms;
  Stator_TimeDomain_SequenceRms(&i0_rms, &i1_rms, &i2_rms);
  const float i1d = fmaxf(i1_rms, eps);
  const float nsr_td = i2_rms / i1d;
  const float zsr_td = i0_rms / i1d;

  float m0_5, m1_5, m2_5;
  float nsr_h5 = 0.f;
  const float f5 = f_supply_hz * 5.0f;
  if (f5 <= fmax) {
    Fortescue_LS_MagAtF(f5, &m0_5, &m1_5, &m2_5);
    nsr_h5 = m2_5 / fmaxf(m1_5, eps);
  }

  const float phase_spread = Stator_PhaseSpreadDeg(f_supply_hz, fs);
  const float odd_harm = Stator_OddHarmIndex(f_supply_hz, fs, fmax);

  g_fault.stator_i0_mag           = m0;
  g_fault.stator_i1_mag           = m1;
  g_fault.stator_i2_mag           = m2;
  g_fault.stator_nsr              = m2 / m1d;
  g_fault.stator_zsr              = m0 / m1d;
  g_fault.stator_imbalance_pct    = imb_pct;
  g_fault.stator_harm_ratio       = harm_ratio;
  g_fault.stator_resid_gnd_ratio  = resid_ratio;
  g_fault.stator_zsr_h3           = zsr_h3;
  g_fault.stator_i0_rms_td        = i0_rms;
  g_fault.stator_i1_rms_td        = i1_rms;
  g_fault.stator_i2_rms_td        = i2_rms;
  g_fault.stator_nsr_td           = nsr_td;
  g_fault.stator_zsr_td           = zsr_td;
  g_fault.stator_nsr_h5           = nsr_h5;
  g_fault.stator_phase_spread_deg = phase_spread;
  g_fault.stator_odd_harm_index   = odd_harm;

  float short_idx = Stator_FuseShortIndex(g_fault.stator_nsr, nsr_td, nsr_h5,
                                          imb_pct, harm_ratio, odd_harm, phase_spread);
  float gnd_idx   = Stator_FuseGndIndex(g_fault.stator_zsr, zsr_td, resid_ratio, zsr_h3);

  short_idx = StatorBaseline_Ratio(SB_NSR, short_idx);
  gnd_idx   = StatorBaseline_Ratio(SB_ZSR, gnd_idx);

  g_fault.stator_short_index = short_idx;
  g_fault.stator_gnd_index   = gnd_idx;

  Stator_UpdateEmaCusum();

  /* Per-metric levels (0 OK, 1 EARLY, 2 WARN, 3 ALARM) */
  uint8_t short_lv = 0u;
  short_lv = Stator_MaxU8(short_lv, Stator_MetricLevel(g_fault.stator_nsr, FP_STATOR_NSR_EARLY,
                         FP_STATOR_NSR_WARN, FP_STATOR_NSR_ALARM));
  short_lv = Stator_MaxU8(short_lv, Stator_MetricLevel(nsr_td, FP_STATOR_NSR_TD_EARLY,
                         FP_STATOR_NSR_TD_WARN, FP_STATOR_NSR_TD_ALARM));
  short_lv = Stator_MaxU8(short_lv, Stator_MetricLevel(nsr_h5, FP_STATOR_NSR_EARLY,
                         FP_STATOR_NSR_WARN, FP_STATOR_NSR_ALARM));
  short_lv = Stator_MaxU8(short_lv, Stator_MetricLevel(imb_pct, FP_STATOR_IMB_EARLY_PCT,
                         FP_STATOR_IMB_WARN_PCT, FP_STATOR_IMB_ALARM_PCT));
  short_lv = Stator_MaxU8(short_lv, Stator_MetricLevel(harm_ratio, FP_STATOR_HARM_EARLY,
                         FP_STATOR_HARM_WARN, FP_STATOR_HARM_ALARM));
  short_lv = Stator_MaxU8(short_lv, Stator_MetricLevel(odd_harm, FP_STATOR_ODD_HARM_EARLY,
                         FP_STATOR_ODD_HARM_WARN, FP_STATOR_ODD_HARM_ALARM));
  short_lv = Stator_MaxU8(short_lv, Stator_MetricLevel(phase_spread, FP_STATOR_PHASE_SPREAD_EARLY,
                         FP_STATOR_PHASE_SPREAD_WARN, FP_STATOR_PHASE_SPREAD_ALARM));
  short_lv = Stator_MaxU8(short_lv, Stator_MetricLevel(g_fault.stator_short_index_ema,
                         FP_STATOR_SHORT_INDEX_EARLY, FP_STATOR_SHORT_INDEX_WARN,
                         FP_STATOR_SHORT_INDEX_ALARM));
  if (g_stator_cusum_short >= FP_STATOR_CUSUM_ALARM) short_lv = Stator_MaxU8(short_lv, 3u);
  else if (g_stator_cusum_short >= FP_STATOR_CUSUM_WARN) short_lv = Stator_MaxU8(short_lv, 2u);
  else if (g_stator_cusum_short >= FP_STATOR_CUSUM_EARLY) short_lv = Stator_MaxU8(short_lv, 1u);

  uint8_t gnd_lv = 0u;
  gnd_lv = Stator_MaxU8(gnd_lv, Stator_MetricLevel(g_fault.stator_zsr, FP_STATOR_ZSR_EARLY,
                       FP_STATOR_ZSR_WARN, FP_STATOR_ZSR_ALARM));
  gnd_lv = Stator_MaxU8(gnd_lv, Stator_MetricLevel(zsr_td, FP_STATOR_ZSR_TD_EARLY,
                       FP_STATOR_ZSR_TD_WARN, FP_STATOR_ZSR_TD_ALARM));
  gnd_lv = Stator_MaxU8(gnd_lv, Stator_MetricLevel(resid_ratio, FP_STATOR_RESID_EARLY,
                       FP_STATOR_RESID_WARN, FP_STATOR_RESID_ALARM));
  gnd_lv = Stator_MaxU8(gnd_lv, Stator_MetricLevel(zsr_h3, FP_STATOR_ZSR_H3_EARLY,
                       FP_STATOR_ZSR_H3_WARN, FP_STATOR_ZSR_H3_ALARM));
  gnd_lv = Stator_MaxU8(gnd_lv, Stator_MetricLevel(g_fault.stator_gnd_index_ema,
                       FP_STATOR_GND_INDEX_EARLY, FP_STATOR_GND_INDEX_WARN,
                       FP_STATOR_GND_INDEX_ALARM));
  if (g_stator_cusum_gnd >= FP_STATOR_CUSUM_ALARM) gnd_lv = Stator_MaxU8(gnd_lv, 3u);
  else if (g_stator_cusum_gnd >= FP_STATOR_CUSUM_WARN) gnd_lv = Stator_MaxU8(gnd_lv, 2u);
  else if (g_stator_cusum_gnd >= FP_STATOR_CUSUM_EARLY) gnd_lv = Stator_MaxU8(gnd_lv, 1u);

  g_fault.stator_early_short = (short_lv == 1u) ? 1u : 0u;
  g_fault.stator_early_gnd   = (gnd_lv == 1u) ? 1u : 0u;
  g_fault.stator_fault_short = short_lv;
  g_fault.stator_fault_gnd   = gnd_lv;
  g_fault.stator_fault_level = Stator_MaxU8(short_lv, gnd_lv);

  if (g_stator_calib_active) {
    StatorBaseline_Accumulate();
    if (g_stator_calib_count >= FP_BASELINE_CALIB_FRAMES) {
      StatorBaseline_Finalize();
      g_stator_calib_active = 0u;
    }
  }
}

/* ============================================================================
 * Teager-Kaiser, LS refinement, MUSIC, Kurtogram, Wavelet
 * ============================================================================
 */

static float LS_Power_Teager_AtF(float f_hz)
{
  return LS_Sinusoid_Power(teager_buf, FP_FFT_SIZE, f_hz, FP_SAMPLE_RATE_HZ);
}

static void Teager_Kaiser_From(const float *x, float *psi, uint32_t N)
{
  if (N < 3u) { for (uint32_t i = 0; i < N; i++) psi[i] = 0.f; return; }
  psi[0] = 0.f;
  for (uint32_t n = 1u; n < N - 1u; n++)
    psi[n] = x[n] * x[n] - x[n - 1u] * x[n + 1u];
  psi[N - 1u] = 0.f;
}

static float LS_Refined_Max_Teager(float f_center, float half_bw_hz, uint32_t steps)
{
  if (steps < 2u) return LS_Power_Teager_AtF(f_center);
  float lo = f_center - half_bw_hz; if (lo < 0.f) lo = 0.f;
  float hi = f_center + half_bw_hz;
  if (hi > 0.45f * FP_SAMPLE_RATE_HZ) hi = 0.45f * FP_SAMPLE_RATE_HZ;
  float mx = 0.f;
  const float denom = (float)(steps - 1u);
  for (uint32_t i = 0; i < steps; i++) {
    const float f = lo + (hi - lo) * (float)i / denom;
    const float p = LS_Power_Teager_AtF(f);
    if (p > mx) mx = p;
  }
  return mx;
}

static void Mean3Phase_Copy(float *dst)
{
  for (uint32_t i = 0; i < FP_FFT_SIZE; i++)
    dst[i] = (phase_a[i] + phase_b[i] + phase_c[i]) / 3.0f;
}

static void LS_Subtract_Line(float *x, uint32_t N, float f_line_hz, float fs)
{
  /* Accurate, leakage-free supply-line projection that stays exact across the
   * full frame (shared double-precision recurrence; see LS_NormalEqs). */
  const double w  = 2.0 * 3.14159265358979323846 * (double)f_line_hz / (double)fs;
  double scc, scs, sss, sxc, sxs;
  LS_NormalEqs(x, N, w, &scc, &scs, &sss, &sxc, &sxs);
  const double det = scc * sss - scs * scs;
  if (fabs(det) < 1e-24) return;
  const double A = (sxc * sss - sxs * scs) / det;
  const double B = (scc * sxs - scs * sxc) / det;
  const double cw = cos(w), sw = sin(w);
  double cn = 1.0, sn = 0.0;
  for (uint32_t n = 0; n < N; n++) {
    x[n] -= (float)(A * cn + B * sn);
    const double cn1 = cn * cw - sn * sw;
    sn = sn * cw + cn * sw; cn = cn1;
  }
}

static void Toeplitz_Autocorr(const float *x, uint32_t N, uint32_t L, float *R)
{
  float r[32];
  if (L > 32u) return;
  for (uint32_t lag = 0; lag < L; lag++) {
    double acc = 0.0;
    const uint32_t M = N - lag;
    for (uint32_t n = 0; n < M; n++) acc += (double)x[n] * (double)x[n + lag];
    r[lag] = (float)(acc / (double)M);
  }
  for (uint32_t i = 0; i < L; i++)
    for (uint32_t j = 0; j < L; j++)
      R[i * L + j] = r[(i > j) ? (i - j) : (j - i)];
}

static void Jacobi_EigenSym(uint32_t n, float *A, float *V)
{
  for (uint32_t i = 0; i < n * n; i++) V[i] = 0.f;
  for (uint32_t i = 0; i < n; i++) V[i * n + i] = 1.f;
  for (int sweep = 0; sweep < 42; sweep++) {
    for (uint32_t p = 0; p < n; p++) {
      for (uint32_t q = p + 1; q < n; q++) {
        const float app = A[p*n+p], aqq = A[q*n+q], apq0 = A[p*n+q];
        if (fabsf(apq0) <= 1e-15f) continue;
        const float tau = (aqq - app) / (2.f * apq0);
        const float t = (tau >= 0.f) ? (1.f / (tau + sqrtf(1.f + tau * tau)))
                                     : (-1.f / (-tau + sqrtf(1.f + tau * tau)));
        const float c_ = 1.f / sqrtf(1.f + t * t), s_ = t * c_;
        for (uint32_t i = 0; i < n; i++) {
          if (i == p || i == q) continue;
          const float aip = A[i*n+p], aiq = A[i*n+q];
          A[i*n+p] = A[p*n+i] = c_ * aip - s_ * aiq;
          A[i*n+q] = A[q*n+i] = s_ * aip + c_ * aiq;
        }
        A[p*n+p] = c_*c_*app - 2.f*c_*s_*apq0 + s_*s_*aqq;
        A[q*n+q] = s_*s_*app + 2.f*c_*s_*apq0 + c_*c_*aqq;
        A[p*n+q] = A[q*n+p] = 0.f;
        for (uint32_t i = 0; i < n; i++) {
          const float vip = V[i*n+p], viq = V[i*n+q];
          V[i*n+p] = c_ * vip - s_ * viq;
          V[i*n+q] = s_ * vip + c_ * viq;
        }
      }
    }
  }
}

static void SortEigenpairsDesc(uint32_t n, float *diag, float *V)
{
  uint32_t order[32]; float Vtmp[32*32], dtmp[32];
  if (n > 32u) return;
  for (uint32_t i = 0; i < n; i++) order[i] = i;
  for (uint32_t i = 0; i < n; i++)
    for (uint32_t j = i + 1; j < n; j++)
      if (diag[order[j]] > diag[order[i]]) { uint32_t t = order[i]; order[i] = order[j]; order[j] = t; }
  for (uint32_t j = 0; j < n; j++) {
    dtmp[j] = fmaxf(diag[order[j]], 1e-20f);
    for (uint32_t i = 0; i < n; i++) Vtmp[i*n+j] = V[i*n+order[j]];
  }
  memcpy(V, Vtmp, n*n*sizeof(float));
  memcpy(diag, dtmp, n*sizeof(float));
}

/* Denominator = sum_{k in noise} |a(f)^H v_k|^2 with a_i(f)=e^{jωi}/√L, v_k real. */
static float MUSIC_P_AtF(const float *Vcols, uint32_t L, uint32_t M, float f_hz, float fs)
{
  if (M >= L) return 0.f;
  const double w  = 2.0 * 3.14159265358979323846 * (double)f_hz / (double)fs;
  const double cw = cos(w), sw = sin(w);
  const float invL = 1.f / (float)L;
  float den = 0.f;
  for (uint32_t k = M; k < L; k++) {
    double cn = 1.0, sn = 0.0;
    float acc_c = 0.f, acc_s = 0.f;
    for (uint32_t i = 0; i < L; i++) {
      const float vik = Vcols[i * L + k];
      acc_c += vik * (float)cn;
      acc_s += vik * (float)sn;
      const double cn1 = cn * cw - sn * sw;
      sn = sn * cw + cn * sw; cn = cn1;
    }
    den += (acc_c * acc_c + acc_s * acc_s) * invL;
  }
  return 1.f / fmaxf(den, 1e-24f);
}

/* ---- Kurtogram: IIR band-pass + Hilbert envelope ---- */

typedef struct { float z1, z2; } BiquadStateF;

static float Biquad_Tick(BiquadStateF *st, float b0, float b1, float b2, float a1, float a2, float x)
{
  const float y = b0 * x + st->z1;
  st->z1 = b1 * x - a1 * y + st->z2;
  st->z2 = b2 * x - a2 * y;
  return y;
}

static void Bandpass_Biquad_Design(float fc_hz, float Q, float fs,
                                   float *ob0, float *ob1, float *ob2, float *oa1, float *oa2)
{
  const float w0 = 2.0f * 3.14159265358979323846f * fc_hz / fs;
  const float sn = sinf(w0), cs = cosf(w0);
  float Qc = Q; if (Qc < 0.08f) Qc = 0.08f;
  const float alpha = sn / (2.0f * Qc);
  const float a0_ = 1.f + alpha;
  *ob0 =  alpha / a0_;
  *ob1 =  0.f;
  *ob2 = -alpha / a0_;
  *oa1 = -2.f * cs / a0_;
  *oa2 = (1.f - alpha) / a0_;
}

static void Kurtogram_InitCoeffs(void)
{
  static const float flo[FP_KURT_NBANDS] = {
    80.f,  220.f,  400.f,  600.f,  900.f, 1150.f,
    1400.f, 1650.f, 1850.f, 2050.f, 2200.f, 2350.f
  };
  static const float fhi[FP_KURT_NBANDS] = {
    220.f,  400.f,  600.f,  900.f, 1150.f, 1400.f,
    1650.f, 1850.f, 2050.f, 2200.f, 2350.f, 2480.f
  };
  for (uint32_t b = 0u; b < FP_KURT_NBANDS; b++) {
    const float fc = 0.5f * (flo[b] + fhi[b]);
    float bw = fhi[b] - flo[b]; if (bw < 25.f) bw = 25.f;
    kb_fc_hz[b] = (uint16_t)(fc + 0.5f);
    float Q = fc / (0.38f * bw);
    if (Q < 0.45f) Q = 0.45f;
    if (Q > 28.f)  Q = 28.f;
    Bandpass_Biquad_Design(fc, Q, FP_SAMPLE_RATE_HZ,
                           &kb_b0[b], &kb_b1[b], &kb_b2[b], &kb_a1[b], &kb_a2[b]);
  }
}

static void Kurtogram_FilterTwoStage(uint32_t band, const float *in, float *out, uint32_t N)
{
  BiquadStateF s0={0}, s1={0};
  for (uint32_t n = 0u; n < N; n++) {
    const float y0 = Biquad_Tick(&s0, kb_b0[band], kb_b1[band], kb_b2[band], kb_a1[band], kb_a2[band], in[n]);
    out[n] = Biquad_Tick(&s1, kb_b0[band], kb_b1[band], kb_b2[band], kb_a1[band], kb_a2[band], y0);
  }
}

/* In-place radix-2 Cooley–Tukey; N = FP_KURT_FFT_N (not fixed at 512). */
static void FFT512_Core(float *re, float *im, int inverse)
{
  const uint32_t N = FP_KURT_FFT_N;
  uint32_t j = 0u;
  for (uint32_t i = 0u; i < N; i++) {
    if (i < j) { float t; t=re[i]; re[i]=re[j]; re[j]=t; t=im[i]; im[i]=im[j]; im[j]=t; }
    uint32_t k = N >> 1u;
    while (k <= j) { j -= k; k >>= 1u; }
    j += k;
  }
  for (uint32_t len = 2u; len <= N; len <<= 1u) {
    const float ang = (inverse ? 1.f : -1.f) * (2.0f * 3.14159265358979323846f / (float)len);
    const float wlenr = cosf(ang), wleni = sinf(ang);
    for (uint32_t i = 0u; i < N; i += len) {
      float wr = 1.f, wi = 0.f;
      const uint32_t hlen = len >> 1u;
      for (uint32_t jj = 0u; jj < hlen; jj++) {
        const uint32_t idx = i + jj, kk = idx + hlen;
        const float tre = wr*re[kk] - wi*im[kk], tim_ = wr*im[kk] + wi*re[kk];
        re[kk] = re[idx] - tre; im[kk] = im[idx] - tim_;
        re[idx] += tre; im[idx] += tim_;
        const float nwr = wr*wlenr - wi*wleni; wi = wr*wleni + wi*wlenr; wr = nwr;
      }
    }
  }
  if (inverse) { const float sc = 1.f/(float)N; for (uint32_t i=0;i<N;i++) { re[i]*=sc; im[i]*=sc; } }
}

/* One-sided magnitude spectrum of x (same length as radix-2 FFT), max-hold per export bin. */
static void FillExportFftMag_bins(const float *x, uint32_t N, float *re, float *im,
                                  float *out_mag, uint32_t nbins)
{
  if (N != FP_KURT_FFT_N || nbins == 0u) return;
  for (uint32_t n = 0u; n < N; n++) { re[n] = x[n]; im[n] = 0.f; }
  FFT512_Core(re, im, 0);
  const uint32_t half = N / 2u;
  for (uint32_t i = 0u; i < nbins; i++) {
    uint32_t k0 = (i * half) / nbins;
    uint32_t k1 = ((i + 1u) * half) / nbins;
    if (k1 <= k0) k1 = k0 + 1u;
    if (k1 > half) k1 = half;
    float m = 0.f;
    for (uint32_t k = k0; k < k1; k++) {
      const float t = re[k] * re[k] + im[k] * im[k];
      if (t > m) m = t;
    }
    out_mag[i] = sqrtf(m);
  }
}

/* One-sided |FFT| magnitude with internal Hamming window.
 * The time-domain signal is kept un-windowed for the parametric/envelope paths
 * (LS, MUSIC, ESPRIT, Teager, kurtogram, wavelet, cyclostationary), so the
 * Hamming taper is applied here only, where a plain DFT magnitude genuinely
 * needs leakage control (drives FFT-band ratios and spectral shaft tracking). */
static void FFT_ComputeOneSidedMag_Half(const float *x, float *mag_half)
{
  if (FP_KURT_FFT_N != FP_FFT_SIZE) return;
  for (uint32_t n = 0u; n < FP_FFT_SIZE; n++) {
    fft_re[n] = x[n] * window_hamming[n];
    fft_im[n] = 0.f;
  }
  FFT512_Core(fft_re, fft_im, 0);
  const uint32_t half = FP_FFT_SIZE / 2u;
  for (uint32_t k = 0u; k < half; k++)
    mag_half[k] = sqrtf(fft_re[k] * fft_re[k] + fft_im[k] * fft_im[k]);
}

/* Parabolic interpolation on uniform |FFT| grid for sub-bin frequency accuracy. */
static float FFT_Mag_ParabolicAtF(const float *mag_half, uint32_t half_n, float f_hz, float df)
{
  if (f_hz < df || f_hz >= 0.45f * FP_SAMPLE_RATE_HZ) return 0.f;
  const float kf = f_hz / df;
  uint32_t k = (uint32_t)lroundf(kf);
  if (k < 1u || (k + 1u) >= half_n) {
    if (k >= half_n) return 0.f;
    return mag_half[k];
  }
  const float y0 = mag_half[k - 1u];
  const float y1 = mag_half[k];
  const float y2 = mag_half[k + 1u];
  const float d = y0 - 2.f * y1 + y2;
  if (fabsf(d) < 1e-20f) return y1;
  const float delta = 0.5f * (y0 - y2) / d;
  const float ypk = y1 - 0.25f * (y0 - y2) * delta;
  return (ypk > y1) ? ypk : y1;
}

static float FFT_MagBand_MaxAtF(const float *mag_half, float f_hz, float df, float bw_hz)
{
  const uint32_t kb = (uint32_t)lroundf(f_hz / df);
  const uint32_t hb = (uint32_t)fmaxf(1.f, ceilf(bw_hz / df));
  float mx = 0.f;
  for (int32_t d = -(int32_t)hb; d <= (int32_t)hb; d++) {
    const int64_t kk = (int64_t)kb + (int64_t)d;
    if (kk < 1 || kk >= (int64_t)FP_HALF_FFT) continue;
    const float m = FFT_Mag_ParabolicAtF(mag_half, FP_HALF_FFT, (float)kk * df, df);
    if (m > mx) mx = m;
  }
  return mx;
}

/* ---- Welch PSD + magnitude-squared coherence (50% overlap Hamming segments) ---- */

static float Welch_SegHammingEnergy(uint32_t seg_len)
{
  double u = 0.0;
  if (seg_len < 2u) return 1.f;
  const float denom = (float)(seg_len - 1u);
  for (uint32_t n = 0u; n < seg_len; n++) {
    const float w = 0.54f - 0.46f * cosf(2.0f * 3.14159265358979323846f * (float)n / denom);
    u += (double)w * (double)w;
  }
  return (float)u;
}

static void Welch_FFT_Segment(const float *x, uint32_t base, uint32_t seg_len)
{
  const float denom = (seg_len > 1u) ? (float)(seg_len - 1u) : 1.f;
  for (uint32_t n = 0u; n < FP_FFT_SIZE; n++) {
    if (n < seg_len) {
      const float w = 0.54f - 0.46f * cosf(2.0f * 3.14159265358979323846f * (float)n / denom);
      fft_re[n] = x[base + n] * w;
    } else {
      fft_re[n] = 0.f;
    }
    fft_im[n] = 0.f;
  }
  FFT512_Core(fft_re, fft_im, 0);
}

static void Welch_PSD_OneSided(const float *x, float *psd_half)
{
  const uint32_t N = FP_FFT_SIZE;
  const uint32_t seg_len = FP_WELCH_SEG_LEN;
  const uint32_t hop = FP_WELCH_HOP;
  const uint32_t half = FP_HALF_FFT;
  const float U = Welch_SegHammingEnergy(seg_len);
  uint32_t n_seg = 0u;

  for (uint32_t k = 0u; k < half; k++) psd_half[k] = 0.f;

  for (uint32_t base = 0u; base + seg_len <= N; base += hop) {
    Welch_FFT_Segment(x, base, seg_len);
    n_seg++;
    for (uint32_t k = 0u; k < half; k++) {
      const float pr = fft_re[k], pi = fft_im[k];
      psd_half[k] += pr * pr + pi * pi;
    }
  }
  if (n_seg == 0u || U <= 1e-30f) return;

  const float scale = 1.f / (U * (float)n_seg);
  for (uint32_t k = 0u; k < half; k++) psd_half[k] *= scale;
  if (half > 2u) {
    for (uint32_t k = 1u; k < half - 1u; k++) psd_half[k] *= 2.f;
  }
}

static void Welch_CoherencePair(const float *x, const float *y, float *coh_half)
{
  float *Pxx = wav_buf_a;
  float *Pyy = wav_buf_a + FP_HALF_FFT;
  float *Re_xy = wav_buf_b;
  float *Im_xy = wav_buf_b + FP_HALF_FFT;
  /* Dedicated segment cross-spectrum scratch (no clobber of |FFT| / cyclic2 buffers). */
  float *Xa_re = welch_xa_re;
  float *Xa_im = welch_xa_im;
  const uint32_t N = FP_FFT_SIZE;
  const uint32_t seg_len = FP_WELCH_SEG_LEN;
  const uint32_t hop = FP_WELCH_HOP;
  const uint32_t half = FP_HALF_FFT;
  const float U = Welch_SegHammingEnergy(seg_len);
  uint32_t n_seg = 0u;

  for (uint32_t k = 0u; k < half; k++) {
    Pxx[k] = 0.f; Pyy[k] = 0.f; Re_xy[k] = 0.f; Im_xy[k] = 0.f;
  }

  for (uint32_t base = 0u; base + seg_len <= N; base += hop) {
    Welch_FFT_Segment(x, base, seg_len);
    for (uint32_t k = 0u; k < half; k++) {
      Xa_re[k] = fft_re[k];
      Xa_im[k] = fft_im[k];
      Pxx[k] += Xa_re[k] * Xa_re[k] + Xa_im[k] * Xa_im[k];
    }
    Welch_FFT_Segment(y, base, seg_len);
    for (uint32_t k = 0u; k < half; k++) {
      const float yr = fft_re[k], yi = fft_im[k];
      Pyy[k] += yr * yr + yi * yi;
      Re_xy[k] += Xa_re[k] * yr + Xa_im[k] * yi;
      Im_xy[k] += Xa_im[k] * yr - Xa_re[k] * yi;
    }
    n_seg++;
  }
  if (n_seg == 0u || U <= 1e-30f) {
    for (uint32_t k = 0u; k < half; k++) coh_half[k] = 1.f;
    return;
  }

  const float scale = 1.f / (U * (float)n_seg);
  for (uint32_t k = 0u; k < half; k++) {
    const float sxx = Pxx[k] * scale;
    const float syy = Pyy[k] * scale;
    const float sxy_re = Re_xy[k] * scale;
    const float sxy_im = Im_xy[k] * scale;
    const float num = sxy_re * sxy_re + sxy_im * sxy_im;
    const float den = sxx * syy;
    coh_half[k] = (den > 1e-30f) ? fminf(num / den, 1.f) : 0.f;
  }
}

static void Welch_Coherence_Min3Ph(const float *a, const float *b, const float *c, float *coh_min)
{
  Welch_CoherencePair(a, b, welch_xa_re);
  Welch_CoherencePair(b, c, welch_xa_im);
  Welch_CoherencePair(a, c, coh_min);
  for (uint32_t k = 0u; k < FP_HALF_FFT; k++) {
    coh_min[k] = fminf(fminf(welch_xa_re[k], welch_xa_im[k]), coh_min[k]);
  }
}

static void Phase_CopyCleanResidual(const float *phase_in, float *phase_out);

static void Welch_PSD_Max3Ph_Clean(float *psd_half)
{
  Phase_CopyCleanResidual(phase_a, wav_buf_a);
  Welch_PSD_OneSided(wav_buf_a, psd_half);
  Phase_CopyCleanResidual(phase_b, wav_buf_b);
  Welch_PSD_OneSided(wav_buf_b, kurt_filt_buf);
  for (uint32_t k = 0u; k < FP_HALF_FFT; k++)
    psd_half[k] = fmaxf(psd_half[k], kurt_filt_buf[k]);
  Phase_CopyCleanResidual(phase_c, wav_buf_b);
  Welch_PSD_OneSided(wav_buf_b, kurt_filt_buf);
  for (uint32_t k = 0u; k < FP_HALF_FFT; k++)
    psd_half[k] = fmaxf(psd_half[k], kurt_filt_buf[k]);
}

static void Welch_Coherence_Min3Ph_Clean(float *coh_min)
{
  Phase_CopyCleanResidual(phase_a, wav_buf_a);
  Phase_CopyCleanResidual(phase_b, wav_buf_b);
  Phase_CopyCleanResidual(phase_c, kurt_filt_buf);
  Welch_Coherence_Min3Ph(wav_buf_a, wav_buf_b, kurt_filt_buf, coh_min);
}

static float PSD_BandMaxAtF(const float *psd_half, float f_hz, float df, float bw_hz)
{
  const uint32_t kb = (uint32_t)lroundf(f_hz / df);
  const uint32_t hb = (uint32_t)fmaxf(1.f, ceilf(bw_hz / df));
  float mx = 0.f;
  for (int32_t d = -(int32_t)hb; d <= (int32_t)hb; d++) {
    const int64_t kk = (int64_t)kb + (int64_t)d;
    if (kk < 0 || kk >= (int64_t)FP_HALF_FFT) continue;
    const float p = psd_half[(uint32_t)kk];
    if (p > mx) mx = p;
  }
  return mx;
}

static float Coherence_BandMeanAtF(const float *coh_half, float f_hz, float df, float bw_hz)
{
  const uint32_t kb = (uint32_t)lroundf(f_hz / df);
  const uint32_t hb = (uint32_t)fmaxf(1.f, ceilf(bw_hz / df));
  float sum = 0.f;
  uint32_t cnt = 0u;
  for (int32_t d = -(int32_t)hb; d <= (int32_t)hb; d++) {
    const int64_t kk = (int64_t)kb + (int64_t)d;
    if (kk < 0 || kk >= (int64_t)FP_HALF_FFT) continue;
    sum += coh_half[(uint32_t)kk];
    cnt++;
  }
  return (cnt > 0u) ? (sum / (float)cnt) : 1.f;
}

static float Coherence_BandMinAtF(const float *coh_half, float f_hz, float df, float bw_hz)
{
  const uint32_t kb = (uint32_t)lroundf(f_hz / df);
  const uint32_t hb = (uint32_t)fmaxf(1.f, ceilf(bw_hz / df));
  float mn = 1.f;
  for (int32_t d = -(int32_t)hb; d <= (int32_t)hb; d++) {
    const int64_t kk = (int64_t)kb + (int64_t)d;
    if (kk < 0 || kk >= (int64_t)FP_HALF_FFT) continue;
    const float c = coh_half[(uint32_t)kk];
    if (c < mn) mn = c;
  }
  return mn;
}

#if FP_HIGH_ACCURACY_EN
/* Refine shaft speed from dominant line in supply-cleaned mean current spectrum. */
static float Refine_ShaftHz_FromSpectrum(const float *mag_half, float df)
{
  const uint32_t k_lo = (uint32_t)ceilf(FP_SHAFT_SPEC_FMIN_HZ / df);
  uint32_t k_hi = (uint32_t)floorf(FP_SHAFT_SPEC_FMAX_HZ / df);
  if (k_hi >= FP_HALF_FFT) k_hi = FP_HALF_FFT - 1u;
  if (k_lo + 2u >= k_hi) return 0.f;

  float pk = 0.f;
  uint32_t kp = k_lo;
  for (uint32_t k = k_lo; k <= k_hi; k++) {
    if (mag_half[k] > pk) {
      pk = mag_half[k];
      kp = k;
    }
  }
  if (pk < 1e-12f) return 0.f;
  {
    const float y0 = mag_half[kp - 1u];
    const float y1 = mag_half[kp];
    const float y2 = mag_half[kp + 1u];
    const float d = y0 - 2.f * y1 + y2;
    float delta_k = 0.f;
    if (fabsf(d) > 1e-20f) {
      delta_k = 0.5f * (y0 - y2) / d;
    }
    return ((float)kp + delta_k) * df;
  }
}

static float Detect_ConfidenceScore(uint8_t np_f, uint8_t np_m, uint8_t np_e,
                                    uint8_t music_ok, uint8_t esprit_ok,
                                    const float pf_raw[4])
{
  uint8_t np_on = np_f;
  uint8_t np_tot = 1u;
  if (music_ok) { np_on += np_m; np_tot++; }
  if (esprit_ok) { np_on += np_e; np_tot++; }
  float conf = (np_tot > 0u) ? ((float)np_on / (float)np_tot) : 0.f;
  conf *= FP_DETECT_CONF_NP_WEIGHT;

  float pf_max = pf_raw[0];
  for (uint32_t i = 1u; i < 4u; i++)
    if (pf_raw[i] > pf_max) pf_max = pf_raw[i];
  float pf_part = fminf(pf_max / 3.5f, 1.f);
  conf += FP_DETECT_CONF_PF_WEIGHT * pf_part;
  if (conf > 1.f) conf = 1.f;
  return conf;
}
#endif /* FP_HIGH_ACCURACY_EN */

/* Second-order cyclostationary MCSA: one-sided |FFT(x^2)| energy per bin (Antoni-style moment spectrum). */
static void Cyclic2_ComputeSqSpectrum(const float *x, uint32_t N, float *re, float *im)
{
  if (N != FP_KURT_FFT_N) return;
  for (uint32_t n = 0u; n < N; n++) {
    const float v = x[n];
    re[n] = v * v;
    im[n] = 0.f;
  }
  Remove_DC(re, N);
  FFT512_Core(re, im, 0);
  const uint32_t half = N / 2u;
  for (uint32_t k = 0u; k < half; k++) {
    const float pr = re[k], pi = im[k];
    g_cyclic2_psq[k] = pr * pr + pi * pi;
  }
}

static void FillExport_FromPsqHalf(const float *psq_half, uint32_t half_n,
                                   float *out_mag, uint32_t nbins)
{
  if (half_n == 0u || nbins == 0u) return;
  for (uint32_t i = 0u; i < nbins; i++) {
    uint32_t k0 = (i * half_n) / nbins;
    uint32_t k1 = ((i + 1u) * half_n) / nbins;
    if (k1 <= k0) k1 = k0 + 1u;
    if (k1 > half_n) k1 = half_n;
    float m = 0.f;
    for (uint32_t k = k0; k < k1; k++) {
      const float t = psq_half[k];
      if (t > m) m = t;
    }
    out_mag[i] = sqrtf(m);
  }
}

/* STFT spectral kurtosis (excess) per export bin. */
static void SpectralKurtosis_ComputeBins(const float *x, uint32_t N, float *out_sk, uint32_t nbins)
{
  static float sk_acc2[USB_EXPORT_GRID_BINS];
  static float sk_acc4[USB_EXPORT_GRID_BINS];

  if (N != FP_KURT_FFT_N || nbins == 0u) return;

  const uint32_t half = N / 2u;
  const uint32_t segs = FP_SK_SEGMENTS;
  const uint32_t seg_len = N / segs;
  if (seg_len < 8u) return;

  for (uint32_t i = 0u; i < nbins; i++) {
    sk_acc2[i] = 0.f;
    sk_acc4[i] = 0.f;
  }

  for (uint32_t s = 0u; s < segs; s++) {
    const uint32_t base = s * seg_len;

    for (uint32_t n = 0u; n < N; n++) {
      fft_re[n] = (n < seg_len) ? x[base + n] : 0.f;
      fft_im[n] = 0.f;
    }
    FFT512_Core(fft_re, fft_im, 0);

    for (uint32_t i = 0u; i < nbins; i++) {
      uint32_t k0 = (i * half) / nbins;
      uint32_t k1 = ((i + 1u) * half) / nbins;
      if (k1 <= k0) k1 = k0 + 1u;
      if (k1 > half) k1 = half;

      float p_sum = 0.f;
      uint32_t cnt = 0u;
      for (uint32_t k = k0; k < k1; k++) {
        const float pr = fft_re[k], pi = fft_im[k];
        p_sum += (pr * pr + pi * pi);
        cnt++;
      }
      const float p = (cnt > 0u) ? (p_sum / (float)cnt) : 0.f;
      sk_acc2[i] += p;
      sk_acc4[i] += p * p;
    }
  }

  for (uint32_t i = 0u; i < nbins; i++) {
    const float m2 = sk_acc2[i] / (float)segs;
    const float m4 = sk_acc4[i] / (float)segs;
    if (m2 <= 1e-30f) {
      out_sk[i] = 0.f;
    } else {
      const float sk = m4 / (m2 * m2) - 2.f;
      out_sk[i] = isfinite(sk) ? sk : 0.f;
    }
  }
}

static float Kurtosis_Excess(const float *x, uint32_t N)
{
  if (N < 8u) return 0.f;
  double sum = 0.0;
  for (uint32_t n = 0u; n < N; n++) sum += (double)x[n];
  const double mu = sum / (double)N;
  double s2 = 0.0, s4 = 0.0;
  for (uint32_t n = 0u; n < N; n++) { double d = (double)x[n]-mu; double d2 = d*d; s2 += d2; s4 += d2*d2; }
  const double v2 = s2 / (double)N;
  if (v2 < 1e-30) return 0.f;
  return (float)((s4 / (double)N) / (v2 * v2) - 3.0);
}

static void Hilbert_Envelope512(const float *x, float *env)
{
  for (uint32_t n = 0u; n < FP_KURT_FFT_N; n++) { fft_re[n] = x[n]; fft_im[n] = 0.f; }
  FFT512_Core(fft_re, fft_im, 0);
  for (uint32_t n = 0u; n < FP_KURT_FFT_N; n++) {
    if (n == 0u || n == FP_KURT_FFT_N/2u) { /* keep */ }
    else if (n <= FP_KURT_FFT_N/2u - 1u) { fft_re[n] *= 2.f; fft_im[n] *= 2.f; }
    else { fft_re[n] = 0.f; fft_im[n] = 0.f; }
  }
  FFT512_Core(fft_re, fft_im, 1);
  for (uint32_t n = 0u; n < FP_KURT_FFT_N; n++)
    env[n] = sqrtf(fft_re[n]*fft_re[n] + fft_im[n]*fft_im[n]);
}

static float LS_Power_Envelope_AtF(const float *env, uint32_t N, float f_hz, float fs)
{
  return LS_Sinusoid_Power(env, N, f_hz, fs);
}

static float LS_Refined_Max_Envelope(const float *env, uint32_t N, float f_center,
                                     float half_bw_hz, uint32_t steps)
{
  if (steps < 2u) return LS_Power_Envelope_AtF(env, N, f_center, FP_SAMPLE_RATE_HZ);
  float lo = f_center - half_bw_hz; if (lo < 0.f) lo = 0.f;
  float hi = f_center + half_bw_hz;
  if (hi > 0.45f * FP_SAMPLE_RATE_HZ) hi = 0.45f * FP_SAMPLE_RATE_HZ;
  float mx = 0.f;
  const float denom = (float)(steps - 1u);
  for (uint32_t i = 0u; i < steps; i++) {
    const float f = lo + (hi - lo) * (float)i / denom;
    const float p = LS_Power_Envelope_AtF(env, N, f, FP_SAMPLE_RATE_HZ);
    if (p > mx) mx = p;
  }
  return mx;
}

/* ---- Single wavelet branch: DWT (db4); CWT not used (cost vs. gain on MCU) ---- */

static const float WAV_DB4_LO[8] = {
  -0.010597401784997278f,  0.032883011666885190f,  0.030841381835986875f,
  -0.187034811718881140f, -0.027983769416983850f,  0.630880767929590900f,
   0.714846570548489800f,  0.230377813308855180f
};
static const float WAV_DB4_HI[8] = {
  -0.230377813308855180f,  0.714846570548489800f, -0.630880767929590900f,
  -0.027983769416983850f,  0.187034811718881140f,  0.030841381835986875f,
  -0.032883011666885190f, -0.010597401784997278f
};

static float Wavelet_SumSq(const float *x, uint32_t n)
{
  double s = 0.0;
  for (uint32_t i = 0u; i < n; i++) s += (double)x[i] * (double)x[i];
  return (float)s;
}

static void DWT_db4_Periodic(const float *x, uint32_t N, float *ca, float *cd)
{
  const uint32_t half = N / 2u;
  if (half == 0u || N < 8u) return;
  for (uint32_t k = 0u; k < half; k++) {
    double sl = 0.0, sh = 0.0;
    for (uint32_t m = 0u; m < 8u; m++) {
      int32_t idx = (int32_t)(2u * k) - (int32_t)m;
      idx = (idx % (int32_t)N + (int32_t)N) % (int32_t)N;
      sl += (double)WAV_DB4_LO[m] * (double)x[(uint32_t)idx];
      sh += (double)WAV_DB4_HI[m] * (double)x[(uint32_t)idx];
    }
    ca[k] = (float)sl;
    cd[k] = (float)sh;
  }
}

static void Wavelet_db4_DetailEnergies5(const float *x, float Ed[5])
{
  memcpy(wav_buf_a, x, FP_FFT_SIZE * sizeof(float));
  uint32_t len = FP_FFT_SIZE;
  for (uint32_t lev = 0u; lev < FP_WAVELET_LEVELS; lev++) {
    DWT_db4_Periodic(wav_buf_a, len, wav_buf_b, wav_buf_b + len/2u);
    Ed[lev] = Wavelet_SumSq(wav_buf_b + len/2u, len/2u);
    len /= 2u;
    if (lev < FP_WAVELET_LEVELS - 1u && len >= 8u)
      memcpy(wav_buf_a, wav_buf_b, len * sizeof(float));
  }
}

static float Wavelet_Bearing_Index_3Ph(void)
{
  Wavelet_db4_DetailEnergies5(phase_a, g_fault.exp_wavelet_Ea);
  Wavelet_db4_DetailEnergies5(phase_b, g_fault.exp_wavelet_Eb);
  Wavelet_db4_DetailEnergies5(phase_c, g_fault.exp_wavelet_Ec);
  float acc[5];
  for (uint32_t i = 0u; i < FP_WAVELET_LEVELS; i++)
    acc[i] = fmaxf(fmaxf(g_fault.exp_wavelet_Ea[i], g_fault.exp_wavelet_Eb[i]), g_fault.exp_wavelet_Ec[i]);
  const float mid = acc[2] + acc[3] + acc[4];
  const float hi  = acc[0] + acc[1];
  return fminf(fmaxf(mid / (hi + 1e-12f), 0.02f), 80.f);
}

static void Kurtogram_SelectEnvelope(const float *mean_i, uint32_t *sel_band, float *best_kurt)
{
  if (!g_kurt_coeffs_ready) { Kurtogram_InitCoeffs(); g_kurt_coeffs_ready = 1u; }
  float kmax = -1.0e30f;
  uint32_t best = 0u;
  for (uint32_t b = 0u; b < FP_KURT_NBANDS; b++) {
    Kurtogram_FilterTwoStage(b, mean_i, kurt_filt_buf, FP_FFT_SIZE);
    Hilbert_Envelope512(kurt_filt_buf, kurt_env_buf);
    const float kex = Kurtosis_Excess(kurt_env_buf, FP_FFT_SIZE);
    if (kex > kmax) { kmax = kex; best = b; }
  }
  *sel_band = best;
  if (best_kurt != NULL) *best_kurt = kmax;
  Kurtogram_FilterTwoStage(best, mean_i, kurt_filt_buf, FP_FFT_SIZE);
  Hilbert_Envelope512(kurt_filt_buf, kurt_env_buf);
}

/* ============================================================================
 * Automatic slip / rotor-speed estimation (load-tracking)
 * ============================================================================
 * The rotor mechanical frequency f_r appears in stator current as airgap-
 * eccentricity / load sidebands around the supply fundamental at f_line ± k·f_r.
 * We scan a physically-bounded f_r window (from the inferred pole-pair count and
 * a plausible slip range) and pick the f_r whose first sideband pair (k=1) is
 * strongest, gated by peak prominence. slip = 1 − f_r / f_sync.
 *
 * This replaces the fixed slip parameter: it tracks load changes and sharpens
 * every bearing fault frequency, since BPFO/BPFI/BSF/FTF all scale with f_r.
 * If no clear sideband pair is found, the user's nominal slip is kept.
 * Uses the accurate LS sinusoid power on the raw (supply-bearing) phase currents.
 */
static uint8_t Infer_PolePairs(float f_line_hz)
{
  const float slip_nom = (g_params.motor_slip > 0.f && g_params.motor_slip < 0.9f)
                         ? g_params.motor_slip : 0.03f;
  const float n_sync_nom = g_params.motor_rpm / (1.0f - slip_nom);   /* synchronous rpm (approx) */
  if (n_sync_nom < 1.0f) return 2u;
  const int32_t pi = (int32_t)lroundf((f_line_hz * 60.0f) / n_sync_nom);
  if (pi < 1) return 1u;
  if (pi > 6) return 6u;
  return (uint8_t)pi;
}

#if FP_SLIP_AUTO_EN
static float SlipSideband_Score(float f_line_hz, float fr)
{
  const float f_lo = f_line_hz - fr;
  const float f_hi = f_line_hz + fr;
  if (f_lo < 1.0f || f_hi >= 0.45f * FP_SAMPLE_RATE_HZ) return 0.f;
  const float p_lo = LS_Power_Max3Ph_AtF(f_lo);
  const float p_hi = LS_Power_Max3Ph_AtF(f_hi);
  /* Geometric mean: requires BOTH sidebands present (eccentricity signature). */
  return sqrtf(fmaxf(p_lo, 0.f) * fmaxf(p_hi, 0.f));
}

static float Estimate_Slip_FromSidebands(float f_line_hz, uint8_t pole_pairs, uint8_t *valid)
{
  *valid = 0u;
  if (pole_pairs < 1u) pole_pairs = 1u;
  const float f_sync = f_line_hz / (float)pole_pairs;
  if (f_sync < 2.0f) return g_params.motor_slip;

  const float fr_hi = f_sync * (1.0f - FP_SLIP_MIN);   /* slip min -> fastest rotor */
  const float fr_lo = f_sync * (1.0f - FP_SLIP_MAX);   /* slip max -> slowest rotor */
  if (fr_lo < 1.0f || fr_hi <= fr_lo) return g_params.motor_slip;

  const uint32_t steps = FP_SLIP_SEARCH_STEPS;
  const float denom = (float)(steps - 1u);
  const float step_hz = (fr_hi - fr_lo) / denom;
  float best_score = 0.f, best_fr = 0.f, score_sum = 0.f;
  uint32_t score_cnt = 0u;

  for (uint32_t i = 0u; i < steps; i++) {
    const float fr = fr_lo + step_hz * (float)i;
    const float score = SlipSideband_Score(f_line_hz, fr);
    score_sum += score; score_cnt++;
    if (score > best_score) { best_score = score; best_fr = fr; }
  }
  if (score_cnt == 0u || best_fr <= 0.f) return g_params.motor_slip;

  /* Peak-prominence gate: a real speed sideband stands clearly above the window mean. */
  const float mean_score = score_sum / (float)score_cnt;
  if (mean_score < 1e-22f || best_score < FP_SLIP_SIDEBAND_MIN_PROM * mean_score)
    return g_params.motor_slip;

  /* Local fine search for sub-step f_r accuracy. */
  if (FP_SLIP_REFINE_STEPS >= 2u) {
    float lo2 = best_fr - step_hz; if (lo2 < fr_lo) lo2 = fr_lo;
    float hi2 = best_fr + step_hz; if (hi2 > fr_hi) hi2 = fr_hi;
    const float d2 = (hi2 - lo2) / (float)(FP_SLIP_REFINE_STEPS - 1u);
    for (uint32_t i = 0u; i < FP_SLIP_REFINE_STEPS; i++) {
      const float fr = lo2 + d2 * (float)i;
      const float score = SlipSideband_Score(f_line_hz, fr);
      if (score > best_score) { best_score = score; best_fr = fr; }
    }
  }

  float slip = 1.0f - best_fr / f_sync;
  if (slip < FP_SLIP_MIN) slip = FP_SLIP_MIN;
  if (slip > FP_SLIP_MAX) slip = FP_SLIP_MAX;
  *valid = 1u;
  return slip;
}
#endif /* FP_SLIP_AUTO_EN */

/* ============================================================================
 * Main bearing fault detection (runs on un-windowed, DC-removed current).
 * Stator_Winding_Analyze() is called in main() before this.
 * ============================================================================
 */

static void Fault_Detect_Bearing(void)
{
  const float df = FP_SAMPLE_RATE_HZ / (float)FP_FFT_SIZE;
  float bpfo, bpfi, bsf, ftf;
  float fr_hz = compute_shaft_frequency_hz();

#if FP_SLIP_AUTO_EN
  /* Auto slip from supply speed-sidebands (uses raw phase currents w/ supply present).
   * Runtime-gated by SLIPAUTO=0/1; when off, the nominal SLIP= parameter is used. */
  if (g_slip_auto_en) {
    const uint8_t p = Infer_PolePairs(g_supply_line_hz);
    uint8_t slip_valid = 0u;
    const float slip_meas = Estimate_Slip_FromSidebands(g_supply_line_hz, p, &slip_valid);
    g_fault.pole_pairs = p;
    g_fault.slip_auto_valid = slip_valid;
    if (slip_valid) {
      if (g_fault.slip_estimated <= 0.f || g_fault.slip_estimated > 0.9f)
        g_fault.slip_estimated = slip_meas;
      else
        g_fault.slip_estimated = (1.f - FP_SLIP_TRACK_BLEND) * g_fault.slip_estimated
                                 + FP_SLIP_TRACK_BLEND * slip_meas;
      fr_hz = (g_supply_line_hz / (float)p) * (1.0f - g_fault.slip_estimated);
    } else {
      g_fault.slip_estimated = g_params.motor_slip;
    }
  } else {
    g_fault.slip_estimated = g_params.motor_slip;
    g_fault.slip_auto_valid = 0u;
    g_fault.pole_pairs = Infer_PolePairs(g_supply_line_hz);
  }
#else
  g_fault.slip_estimated = g_params.motor_slip;
  g_fault.slip_auto_valid = 0u;
  g_fault.pole_pairs = Infer_PolePairs(g_supply_line_hz);
#endif

  Bearing_CalcFaultFundamentalsAt(fr_hz, &bpfo, &bpfi, &bsf, &ftf);

  g_fault.shaft_hz_refined = fr_hz;
  g_fault.detect_confidence = 0.f;
  g_fault.index_fft = 1.f;

  g_fault.bpfo_hz = bpfo;
  g_fault.bpfi_hz = bpfi;
  g_fault.bsf_hz  = bsf;
  g_fault.ftf_hz  = ftf;

  uint32_t k_base_min = (uint32_t)ceilf(FP_BASELINE_F1_HZ / df);
  uint32_t k_base_max = (uint32_t)floorf(FP_BASELINE_F2_HZ / df);
  if (k_base_min < 1u) k_base_min = 1u;
  if (k_base_max >= FP_HALF_FFT) k_base_max = FP_HALF_FFT - 1u;
  const uint32_t H = (g_params.harmonic_orders > 0u) ? (uint32_t)g_params.harmonic_orders : 1u;
  const float bw_hz = g_params.bandwidth_hz;

  /* Mean current -> Teager */
  Mean3Phase_Copy(sig_res_mean);
  Teager_Kaiser_From(sig_res_mean, teager_buf, FP_FFT_SIZE);

  /* Kurtogram */
  uint32_t kurt_sel = 0u;
  float kurt_peak = 0.f;
  Kurtogram_SelectEnvelope(sig_res_mean, &kurt_sel, &kurt_peak);
  g_fault.kurt_band_fc_hz = kb_fc_hz[kurt_sel];
  (void)kurt_peak;

  /* Supply line cleaning */
  for (uint32_t h = 1u; h <= FP_SUPPLY_HARMONICS_REMOVE; h++)
    LS_Subtract_Line(sig_res_mean, FP_FFT_SIZE, g_supply_line_hz * (float)h, FP_SAMPLE_RATE_HZ);

  Cyclic2_ComputeSqSpectrum(sig_res_mean, FP_FFT_SIZE, fft_re, fft_im);
  FFT_ComputeOneSidedMag_Half(sig_res_mean, g_fft_mag_half);
  Welch_PSD_Max3Ph_Clean(g_welch_psd_half);
  Welch_Coherence_Min3Ph_Clean(g_coh_min_half);

#if FP_HIGH_ACCURACY_EN
  /* Fallback shaft refine from the spectrum peak — only when the (more reliable,
   * load-tracking) sideband slip estimate was not accepted this frame. */
  if (!g_fault.slip_auto_valid) {
    const float fr_spec = Refine_ShaftHz_FromSpectrum(g_fft_mag_half, df);
    if (fr_spec >= FP_SHAFT_SPEC_FMIN_HZ && fr_spec <= FP_SHAFT_SPEC_FMAX_HZ) {
      fr_hz = fr_hz * (1.f - FP_SHAFT_TRACK_BLEND) + fr_spec * FP_SHAFT_TRACK_BLEND;
      g_fault.shaft_hz_refined = fr_hz;
      Bearing_CalcFaultFundamentalsAt(fr_hz, &bpfo, &bpfi, &bsf, &ftf);
      g_fault.bpfo_hz = bpfo;
      g_fault.bpfi_hz = bpfi;
      g_fault.bsf_hz  = bsf;
      g_fault.ftf_hz  = ftf;
    }
  }
#endif

  /* MUSIC subspace */
  uint8_t music_ok = 0u;
  float evals[32];
  if (FP_FFT_SIZE >= FP_SUB_L) {
    Toeplitz_Autocorr(sig_res_mean, FP_FFT_SIZE, FP_SUB_L, sub_R);
    memcpy(sub_A, sub_R, sizeof(sub_R));
    Jacobi_EigenSym(FP_SUB_L, sub_A, sub_V);
    for (uint32_t i = 0; i < FP_SUB_L; i++) evals[i] = sub_A[i * FP_SUB_L + i];
    SortEigenpairsDesc(FP_SUB_L, evals, sub_V);
    music_ok = 1u;
  }

  /* LS-ESPRIT + TLS-ESPRIT (prefer TLS when it yields more peaks) */
  uint8_t esprit_ok = 0u;
  float esprit_f[ESPRIT_MAX_P];
  uint32_t esprit_n = 0u;
  if (music_ok) {
    float esprit_tls[ESPRIT_MAX_P];
    uint32_t esprit_tls_n = 0u;
    if (ESPRIT_FreqEstimates_TLS(sub_V, FP_SUB_L, FP_SUB_M, FP_SAMPLE_RATE_HZ,
                                 esprit_tls, ESPRIT_MAX_P, &esprit_tls_n) == 0 &&
        esprit_tls_n > 0u) {
      esprit_n = esprit_tls_n;
      memcpy(esprit_f, esprit_tls, esprit_n * sizeof(float));
      esprit_ok = 1u;
    } else if (ESPRIT_FreqEstimates_LS(sub_V, FP_SUB_L, FP_SUB_M, FP_SAMPLE_RATE_HZ,
                                      esprit_f, ESPRIT_MAX_P, &esprit_n) == 0 &&
               esprit_n > 0u) {
      esprit_ok = 1u;
    }
  }
  const float sigma_ep = fmaxf(df, g_params.bandwidth_hz * 0.25f);

  /* Accumulate baseline (non-fault) power — LS uses per-phase max fusion */
  float ls_base_sum=0, mu_base_sum=0, ep_base_sum=0, tg_base_sum=0, sk_base_sum=0;
  float sb_base_sum=0, acf_base_sum=0, welch_base_sum=0, coh_base_sum=0;
  float fft_base_sum = 0.f;
  uint32_t ls_base_cnt=0, mu_base_cnt=0, ep_base_cnt=0, tg_base_cnt=0, sk_base_cnt=0;
  uint32_t sb_base_cnt=0, acf_base_cnt=0, welch_base_cnt=0, coh_base_cnt=0;
  uint32_t fft_base_cnt = 0u;
  float cy_base_sum = 0.f;
  uint32_t cy_base_cnt = 0u;

  for (uint32_t k = k_base_min; k <= k_base_max; k++) {
    const float f = (float)k * df;
    uint8_t skip = 0u;
    for (uint32_t h = 0u; h < H; h++) {
      const float mult = (float)(h + 1u);
      if (fabsf(f - bpfo*mult) <= bw_hz || fabsf(f - bpfi*mult) <= bw_hz ||
          fabsf(f - ftf*mult) <= bw_hz  || fabsf(f - bsf*mult) <= bw_hz)
      { skip = 1u; break; }
    }
    if (!skip) {
      uint8_t skip_cy = 0u;
      for (uint32_t hh = 0u; hh < H; hh++) {
        const float mult = (float)(hh + 1u);
        const float f2a = 2.f * bpfo * mult;
        const float f2b = 2.f * bpfi * mult;
        const float f2c = 2.f * ftf * mult;
        const float f2d = 2.f * bsf * mult;
        if (fabsf(f - f2a) <= bw_hz || fabsf(f - f2b) <= bw_hz || fabsf(f - f2c) <= bw_hz || fabsf(f - f2d) <= bw_hz)
          { skip_cy = 1u; break; }
      }
      if (!skip_cy) {
        for (uint32_t hh = 1u; hh <= FP_SUPPLY_HARMONICS_REMOVE; hh++) {
          const float f2s = 2.f * g_supply_line_hz * (float)hh;
          if (fabsf(f - f2s) <= bw_hz) { skip_cy = 1u; break; }
        }
      }
      if (!skip_cy) {
        cy_base_sum += g_cyclic2_psq[k];
        cy_base_cnt++;
      }
    }
    if (skip) continue;
    ls_base_sum += LS_Power_Max3Ph_AtF(f); ls_base_cnt++;
    tg_base_sum += LS_Power_Teager_AtF(f); tg_base_cnt++;
    sk_base_sum += LS_Power_Envelope_AtF(kurt_env_buf, FP_FFT_SIZE, f, FP_SAMPLE_RATE_HZ); sk_base_cnt++;
    sb_base_sum += Sideband_Power_Max3Ph(g_supply_line_hz, f); sb_base_cnt++;
    acf_base_sum += Envelope_Acf_PeakAtF(kurt_env_buf, FP_FFT_SIZE, f, FP_SAMPLE_RATE_HZ); acf_base_cnt++;
    welch_base_sum += PSD_BandMaxAtF(g_welch_psd_half, f, df, bw_hz); welch_base_cnt++;
    coh_base_sum += Coherence_BandMeanAtF(g_coh_min_half, f, df, bw_hz); coh_base_cnt++;
    fft_base_sum += FFT_MagBand_MaxAtF(g_fft_mag_half, f, df, bw_hz); fft_base_cnt++;
    if (music_ok) { mu_base_sum += MUSIC_P_AtF(sub_V, FP_SUB_L, FP_SUB_M, f, FP_SAMPLE_RATE_HZ); mu_base_cnt++; }
    if (esprit_ok) { ep_base_sum += ESPRIT_ProxyAtF(f, esprit_f, esprit_n, sigma_ep, FP_SAMPLE_RATE_HZ); ep_base_cnt++; }
  }

  /* Accumulate fault-frequency power (per-phase max LS + sideband + ACF) */
  float ls_fault_sum=0, mu_fault_sum=0, ep_fault_sum=0, tg_fault_sum=0, sk_fault_sum=0;
  float sb_fault_sum=0, acf_fault_sum=0, welch_fault_sum=0, coh_fault_sum=0;
  float fft_fault_sum = 0.f;
  uint32_t ls_fault_cnt=0, mu_fault_cnt=0, ep_fault_cnt=0, tg_fault_cnt=0, sk_fault_cnt=0;
  uint32_t sb_fault_cnt=0, acf_fault_cnt=0, welch_fault_cnt=0, coh_fault_cnt=0;
  uint32_t fft_fault_cnt = 0u;

  for (uint32_t h = 0u; h < H; h++) {
    const float mult = (float)(h + 1u);
    const float freqs[4] = { bpfo*mult, bpfi*mult, ftf*mult, bsf*mult };
    for (uint32_t t = 0u; t < 4u; t++) {
      if ((int32_t)lroundf(freqs[t] / df) < 1) continue;
      ls_fault_sum += LS_Refined_Max_Max3Ph(freqs[t], bw_hz, FP_FAULT_FREQ_REFINE_STEPS); ls_fault_cnt++;
      tg_fault_sum += LS_Refined_Max_Teager(freqs[t], bw_hz, FP_FAULT_FREQ_REFINE_STEPS); tg_fault_cnt++;
      sk_fault_sum += LS_Refined_Max_Envelope(kurt_env_buf, FP_FFT_SIZE, freqs[t], bw_hz,
                                              FP_FAULT_FREQ_REFINE_STEPS); sk_fault_cnt++;
      sb_fault_sum += Sideband_Power_Max3Ph(g_supply_line_hz, freqs[t]); sb_fault_cnt++;
      acf_fault_sum += Envelope_Acf_PeakAtF(kurt_env_buf, FP_FFT_SIZE, freqs[t], FP_SAMPLE_RATE_HZ); acf_fault_cnt++;
      welch_fault_sum += PSD_BandMaxAtF(g_welch_psd_half, freqs[t], df, bw_hz); welch_fault_cnt++;
      coh_fault_sum += Coherence_BandMinAtF(g_coh_min_half, freqs[t], df, bw_hz); coh_fault_cnt++;
      fft_fault_sum += FFT_MagBand_MaxAtF(g_fft_mag_half, freqs[t], df, bw_hz); fft_fault_cnt++;
      if (music_ok) { mu_fault_sum += MUSIC_P_AtF(sub_V, FP_SUB_L, FP_SUB_M, freqs[t], FP_SAMPLE_RATE_HZ); mu_fault_cnt++; }
      if (esprit_ok) { ep_fault_sum += ESPRIT_ProxyAtF(freqs[t], esprit_f, esprit_n, sigma_ep, FP_SAMPLE_RATE_HZ); ep_fault_cnt++; }
    }
  }

  float cy_fault_sum = 0.f;
  uint32_t cy_fault_cnt = 0u;
  const uint32_t hb_cyc = (uint32_t)fmaxf(1.f, bw_hz / df);
  for (uint32_t h = 0u; h < H; h++) {
    const float mult = (float)(h + 1u);
    const float freqs_c[4] = { bpfo * mult, bpfi * mult, ftf * mult, bsf * mult };
    for (uint32_t t = 0u; t < 4u; t++) {
      const float f2 = 2.f * freqs_c[t];
      if (f2 < df || f2 >= 0.45f * FP_SAMPLE_RATE_HZ) continue;
      const uint32_t kc = (uint32_t)lroundf(f2 / df);
      float band = 0.f;
      for (int32_t d = -(int32_t)hb_cyc; d <= (int32_t)hb_cyc; d++) {
        const int64_t kk = (int64_t)kc + (int64_t)d;
        if (kk < 1 || kk >= (int64_t)FP_HALF_FFT) continue;
        band += g_cyclic2_psq[(uint32_t)kk];
      }
      cy_fault_sum += band;
      cy_fault_cnt++;
    }
  }

  /* Compute ratios */
  const float ls_base_m = (ls_base_cnt>0) ? ls_base_sum/(float)ls_base_cnt : 0.f;
  const float mu_base_m = (mu_base_cnt>0) ? mu_base_sum/(float)mu_base_cnt : 0.f;
  const float tg_base_m = (tg_base_cnt>0) ? tg_base_sum/(float)tg_base_cnt : 0.f;
  const float sk_base_m = (sk_base_cnt>0) ? sk_base_sum/(float)sk_base_cnt : 0.f;
  const float sb_base_m = (sb_base_cnt>0) ? sb_base_sum/(float)sb_base_cnt : 0.f;
  const float acf_base_m = (acf_base_cnt>0) ? acf_base_sum/(float)acf_base_cnt : 0.f;
  const float welch_base_m = (welch_base_cnt > 0u) ? (welch_base_sum / (float)welch_base_cnt) : 0.f;
  const float coh_base_m = (coh_base_cnt > 0u) ? (coh_base_sum / (float)coh_base_cnt) : 1.f;
  const float fft_base_m = (fft_base_cnt > 0u) ? (fft_base_sum / (float)fft_base_cnt) : 0.f;

  float r_ls  = (ls_base_m > 1e-18f) ? (ls_fault_cnt>0 ? ls_fault_sum/(float)ls_fault_cnt : 0.f) / ls_base_m : 0.f;
  float r_fft = 1.f;
  if (fft_base_m > 1e-18f && fft_fault_cnt > 0u) {
    r_fft = (fft_fault_sum / (float)fft_fault_cnt) / fft_base_m;
  }
#if FP_FOURIER_LS_FFT_BLEND
  {
    const float r_ls_raw = r_ls;
    r_ls = sqrtf(fmaxf(r_ls_raw, 1e-12f) * fmaxf(r_fft, 1e-12f));
  }
#endif
  float r_mu = 1.f;
  if (music_ok && mu_base_m > 1e-18f && mu_fault_cnt > 0u)
    r_mu = (mu_fault_sum/(float)mu_fault_cnt) / mu_base_m;
  const float ep_base_m = (ep_base_cnt>0) ? ep_base_sum/(float)ep_base_cnt : 0.f;
  float r_ep = 1.f;
  if (esprit_ok && ep_base_m > 1e-18f && ep_fault_cnt > 0u)
    r_ep = (ep_fault_sum/(float)ep_fault_cnt) / ep_base_m;
  float r_tg  = (tg_base_m > 1e-24f) ? (tg_fault_cnt>0 ? tg_fault_sum/(float)tg_fault_cnt : 1.f) / tg_base_m : 1.f;
  float r_sk  = (sk_base_m > 1e-24f) ? (sk_fault_cnt>0 ? sk_fault_sum/(float)sk_fault_cnt : 1.f) / sk_base_m : 1.f;
  float r_sb  = (sb_base_m > 1e-24f) ? (sb_fault_cnt>0 ? sb_fault_sum/(float)sb_fault_cnt : 1.f) / sb_base_m : 1.f;
  float r_acf = (acf_base_m > 1e-24f) ? (acf_fault_cnt>0 ? acf_fault_sum/(float)acf_fault_cnt : 1.f) / acf_base_m : 1.f;
  float r_welch = 1.f;
  if (welch_base_m > 1e-30f && welch_fault_cnt > 0u)
    r_welch = (welch_fault_sum / (float)welch_fault_cnt) / welch_base_m;
  float r_coh = 1.f;
  if (coh_fault_cnt > 0u) {
    const float coh_fault_m = coh_fault_sum / (float)coh_fault_cnt;
    r_coh = coh_base_m / fmaxf(coh_fault_m, 0.02f);
  }
  const float r_wav = Wavelet_Bearing_Index_3Ph();
  const float cy_base_m = (cy_base_cnt > 0u) ? (cy_base_sum / (float)cy_base_cnt) : 0.f;
  float r_cyc = 1.f;
  if (cy_base_m > 1e-30f && cy_fault_cnt > 0u)
    r_cyc = (cy_fault_sum / (float)cy_fault_cnt) / cy_base_m;

  /* Adaptive healthy-motor baseline normalization */
  r_ls  = Baseline_Normalize(BL_LS, r_ls);
  r_mu  = Baseline_Normalize(BL_MU, r_mu);
  r_ep  = Baseline_Normalize(BL_EP, r_ep);
  r_tg  = Baseline_Normalize(BL_TG, r_tg);
  r_sk  = Baseline_Normalize(BL_SK, r_sk);
  r_cyc = Baseline_Normalize(BL_CY, r_cyc);
  r_sb  = Baseline_Normalize(BL_SB, r_sb);
  r_acf = Baseline_Normalize(BL_ACF, r_acf);

  g_fault.index_ls       = r_ls;
  g_fault.index_music    = r_mu;
  g_fault.index_esprit   = r_ep;
  g_fault.index_fft      = r_fft;

  /* Mean amplitudes (before baseline normalize) for NP + USB */
  g_fault.amp_fourier_fault = (ls_fault_cnt > 0u) ? (ls_fault_sum / (float)ls_fault_cnt) : 0.f;
  g_fault.amp_fourier_base  = ls_base_m;
  /* Native |FFT| amplitudes stored separately for diagnostics */
  if (fft_fault_cnt > 0u && fft_base_m > 1e-18f) {
    g_fault.amp_fourier_fault = fmaxf(g_fault.amp_fourier_fault,
                                      fft_fault_sum / (float)fft_fault_cnt);
  }
  g_fault.amp_music_fault   = (music_ok && mu_fault_cnt > 0u) ? (mu_fault_sum / (float)mu_fault_cnt) : 0.f;
  g_fault.amp_music_base    = mu_base_m;
  g_fault.amp_esprit_fault  = (esprit_ok && ep_fault_cnt > 0u) ? (ep_fault_sum / (float)ep_fault_cnt) : 0.f;
  g_fault.amp_esprit_base   = ep_base_m;

  /* Per-method Neyman-Pearson on amplitude ratio (Fourier/LS, MUSIC, ESPRIT) */
  g_fault.np_alarm_fourier = NP_TestAmplitudeRatio(r_ls, ls_fault_cnt, ls_base_cnt,
                                                    FP_NP_PFA, &g_fault.np_gamma_fourier);
  g_fault.np_alarm_music = 0u;
  g_fault.np_gamma_music = 1.0e30f;
  if (music_ok) {
    g_fault.np_alarm_music = NP_TestAmplitudeRatio(r_mu, mu_fault_cnt, mu_base_cnt,
                                                  FP_NP_PFA, &g_fault.np_gamma_music);
  }
  g_fault.np_alarm_esprit = 0u;
  g_fault.np_gamma_esprit = 1.0e30f;
  if (esprit_ok) {
    g_fault.np_alarm_esprit = NP_TestAmplitudeRatio(r_ep, ep_fault_cnt, ep_base_cnt,
                                                     FP_NP_PFA, &g_fault.np_gamma_esprit);
  }

  g_fault.index_teager   = r_tg;
  g_fault.index_sk       = r_sk;
  g_fault.index_wavelet  = r_wav;
  g_fault.index_cyclic   = r_cyc;
  g_fault.index_sideband = r_sb;
  g_fault.index_env_acf  = r_acf;
  g_fault.index_welch    = r_welch;
  g_fault.index_coherence = r_coh;

  /* Per-fault-type fused scores (BPFO / BPFI / BSF / FTF) */
  const float fault_hz[4] = { bpfo, bpfi, bsf, ftf };
  float pf_raw[4] = {0.f, 0.f, 0.f, 0.f};
  for (uint32_t t = 0u; t < 4u; t++) {
    float ls_f = 0.f, sb_f = 0.f, acf_f = 0.f, cy_f = 0.f, mu_f = 0.f, ep_f = 0.f;
    uint32_t ls_n = 0u, sb_n = 0u, acf_n = 0u, cy_n = 0u;
    for (uint32_t h = 0u; h < H; h++) {
      const float ff = fault_hz[t] * (float)(h + 1u);
      if ((int32_t)lroundf(ff / df) < 1) continue;
      ls_f += LS_Refined_Max_Max3Ph(ff, bw_hz, FP_FAULT_FREQ_REFINE_STEPS); ls_n++;
      sb_f += Sideband_Power_Max3Ph(g_supply_line_hz, ff); sb_n++;
      acf_f += Envelope_Acf_PeakAtF(kurt_env_buf, FP_FFT_SIZE, ff, FP_SAMPLE_RATE_HZ); acf_n++;
      const float f2 = 2.f * ff;
      if (f2 >= df && f2 < 0.45f * FP_SAMPLE_RATE_HZ) {
        const uint32_t kc = (uint32_t)lroundf(f2 / df);
        float band = 0.f;
        for (int32_t d = -(int32_t)hb_cyc; d <= (int32_t)hb_cyc; d++) {
          const int64_t kk = (int64_t)kc + (int64_t)d;
          if (kk < 1 || kk >= (int64_t)FP_HALF_FFT) continue;
          band += g_cyclic2_psq[(uint32_t)kk];
        }
        cy_f += band; cy_n++;
      }
      if (music_ok) {
        const float r = MUSIC_P_AtF(sub_V, FP_SUB_L, FP_SUB_M, ff, FP_SAMPLE_RATE_HZ) /
                        fmaxf(mu_base_m, 1e-18f);
        if (r > mu_f) mu_f = r;
      }
      if (esprit_ok) {
        const float r = ESPRIT_ProxyAtF(ff, esprit_f, esprit_n, sigma_ep, FP_SAMPLE_RATE_HZ) /
                        fmaxf(ep_base_m, 1e-18f);
        if (r > ep_f) ep_f = r;
      }
    }
    const float r_ls_f  = (ls_n > 0u && ls_base_m > 1e-18f) ? (ls_f / (float)ls_n) / ls_base_m : 0.f;
    const float r_sb_f  = (sb_n > 0u && sb_base_m > 1e-24f) ? (sb_f / (float)sb_n) / sb_base_m : 1.f;
    const float r_acf_f = (acf_n > 0u && acf_base_m > 1e-24f) ? (acf_f / (float)acf_n) / acf_base_m : 1.f;
    const float r_cy_f  = (cy_n > 0u && cy_base_m > 1e-30f) ? (cy_f / (float)cy_n) / cy_base_m : 1.f;
    if (mu_f < 1e-6f) mu_f = 1.f;
    if (ep_f < 1e-6f) ep_f = 1.f;
    pf_raw[t] = FusePerFaultScore(r_ls_f, r_sb_f, r_acf_f, r_cy_f, mu_f, ep_f, music_ok, esprit_ok);
  }

  pf_raw[0] = Baseline_Normalize(BL_BPFO, pf_raw[0]);
  pf_raw[1] = Baseline_Normalize(BL_BPFI, pf_raw[1]);
  pf_raw[2] = Baseline_Normalize(BL_BSF,  pf_raw[2]);
  pf_raw[3] = Baseline_Normalize(BL_FTF,  pf_raw[3]);

  g_fault.index_bpfo = pf_raw[0];
  g_fault.index_bpfi = pf_raw[1];
  g_fault.index_bsf  = pf_raw[2];
  g_fault.index_ftf  = pf_raw[3];
  g_fault.dominant_fault = PickDominantFault(pf_raw);

  /* Weighted global fusion + per-fault max boost */
  float raw_fault_index = WeightedGlobalFusion(r_ls, r_mu, r_ep, r_tg, r_sk, r_wav, r_cyc,
                                               r_sb, r_acf, r_welch, r_coh,
                                               music_ok, esprit_ok);
  {
    float pf_max = fmaxf(fmaxf(pf_raw[0], pf_raw[1]), fmaxf(pf_raw[2], pf_raw[3]));
    raw_fault_index = fmaxf(raw_fault_index, pf_max * 0.85f);
  }
#if FP_HIGH_ACCURACY_EN
  g_fault.detect_confidence = Detect_ConfidenceScore(
      g_fault.np_alarm_fourier, g_fault.np_alarm_music, g_fault.np_alarm_esprit,
      music_ok, esprit_ok, pf_raw);
  raw_fault_index *= (0.88f + 0.12f * g_fault.detect_confidence);
#endif

  /* Multi-frame average of fused index */
  {
    static float fi_ring[FP_FAULT_INDEX_INTEGRATE_FRAMES];
    static uint32_t fi_idx = 0u;
    static uint32_t fi_n = 0u;
    fi_ring[fi_idx % FP_FAULT_INDEX_INTEGRATE_FRAMES] = raw_fault_index;
    fi_idx++;
    if (fi_n < FP_FAULT_INDEX_INTEGRATE_FRAMES) fi_n++;
    double acc = 0.0;
    if (fi_n < FP_FAULT_INDEX_INTEGRATE_FRAMES) {
      for (uint32_t k = 0u; k < fi_n; k++) acc += (double)fi_ring[k];
      g_fault.fault_index = (float)(acc / (double)fi_n);
    } else {
      for (uint32_t k = 0u; k < FP_FAULT_INDEX_INTEGRATE_FRAMES; k++) acc += (double)fi_ring[k];
      g_fault.fault_index = (float)(acc / (double)FP_FAULT_INDEX_INTEGRATE_FRAMES);
    }
  }

  Update_FaultIndex_Ema_Cusum();

  /* Healthy-motor CALIB accumulation */
  if (g_baseline_calib_active) {
    Baseline_AccumulateFrame(pf_raw);
    if (g_baseline_calib_count >= FP_BASELINE_CALIB_FRAMES) {
      Baseline_FinalizeFromAccum();
      g_baseline_calib_active = 0u;
    }
  }

  /* Bearing level: NP (Fourier+MUSIC+ESPRIT), fused index, CUSUM, confidence */
  {
    const float decide = fmaxf(g_fault.fault_index_ema, g_fault.fault_index);
    uint8_t np_votes = g_fault.np_alarm_fourier;
    if (music_ok && g_fault.np_alarm_music) np_votes++;
    if (esprit_ok && g_fault.np_alarm_esprit) np_votes++;

    uint8_t np_strong = 0u;
    if (g_fault.np_alarm_fourier &&
        r_ls > g_fault.np_gamma_fourier * FP_NP_STRONG_RATIO_MULT) {
      np_strong = 1u;
    }
    if (music_ok && g_fault.np_alarm_music &&
        r_mu > g_fault.np_gamma_music * FP_NP_STRONG_RATIO_MULT) {
      np_strong = 1u;
    }
    if (esprit_ok && g_fault.np_alarm_esprit &&
        r_ep > g_fault.np_gamma_esprit * FP_NP_STRONG_RATIO_MULT) {
      np_strong = 1u;
    }

    const uint8_t np_alarm = (np_votes >= FP_NP_ALARM_MIN_VOTES || np_strong) ? 1u : 0u;
    const uint8_t np_fused = (ls_base_cnt > 1u && ls_fault_cnt > 0u &&
                              decide > g_fault.np_gamma_fourier) ? 1u : 0u;
    const uint8_t cusum_alarm = (g_fault.cusum_score > FP_CUSUM_ALARM) ? 1u : 0u;

    if (np_alarm || np_fused || cusum_alarm) {
      g_fault.fault_level = 2u;
    } else if (g_fault.fault_index_ema > g_params.warning_threshold ||
               g_fault.cusum_score > FP_CUSUM_ALARM * 0.45f ||
               (g_fault.detect_confidence > 0.55f && np_votes >= 1u)) {
      g_fault.fault_level = 1u;
    } else {
      g_fault.fault_level = 0u;
    }
  }

  /* USB plot buffers (2048-bin grids); spectral CSV uses full N/2 via USB_EXPORT_NATIVE_BINS */
  FillExport_FromPsqHalf(g_cyclic2_psq, FP_HALF_FFT, g_usb_export_cyclic2, USB_EXPORT_GRID_BINS);
  FillExportFftMag_bins(sig_res_mean, FP_FFT_SIZE, fft_re, fft_im,
                        g_usb_export_fft, USB_EXPORT_GRID_BINS);
  SpectralKurtosis_ComputeBins(sig_res_mean, FP_FFT_SIZE, g_usb_export_sk, USB_EXPORT_GRID_BINS);
  if (music_ok) {
    for (uint32_t i = 0u; i < USB_EXPORT_GRID_BINS; i++) {
      const float f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)USB_EXPORT_GRID_BINS;
      g_usb_export_music[i] = MUSIC_P_AtF(sub_V, FP_SUB_L, FP_SUB_M, f_hz, FP_SAMPLE_RATE_HZ);
    }
  } else {
    for (uint32_t i = 0u; i < USB_EXPORT_GRID_BINS; i++) g_usb_export_music[i] = 0.f;
  }
  g_fault.exp_music_ok = music_ok;
  g_fault.exp_esprit_ok = esprit_ok;
  if (esprit_ok && esprit_n > 0u) {
    uint32_t ncp = esprit_n;
    if (ncp > 16u) ncp = 16u;
    g_fault.exp_esprit_n = (uint8_t)ncp;
    for (uint32_t i = 0u; i < ncp; i++) g_fault.exp_esprit_f_hz[i] = esprit_f[i];
    for (uint32_t i = ncp; i < 16u; i++) g_fault.exp_esprit_f_hz[i] = 0.f;
  } else {
    g_fault.exp_esprit_n = 0u;
    for (uint32_t i = 0u; i < 16u; i++) g_fault.exp_esprit_f_hz[i] = 0.f;
  }
  if (music_ok) {
    for (uint32_t i = 0u; i < FP_SUB_L; i++) g_fault.exp_music_eval[i] = evals[i];
    for (uint32_t i = FP_SUB_L; i < 32u; i++) g_fault.exp_music_eval[i] = 0.f;
  } else {
    for (uint32_t i = 0u; i < 32u; i++) g_fault.exp_music_eval[i] = 0.f;
  }
}

/* ============================================================================
 * ADC conversion + DMA callbacks
 * ============================================================================
 */

static float Phase_Rms_A(const float *x, uint32_t n)
{
  double s = 0.0;
  for (uint32_t i = 0u; i < n; i++) { double v = (double)x[i]; s += v*v; }
  return (float)sqrt(s / (double)n);
}

static void ADC_Raw_To_Current(void)
{
  const double inv_fs = 1.0 / (double)FP_ADC_RESOLUTION_F;
  const double vref = (double)FP_VREF;
  const double sens = (double)FP_CURRENT_SENSITIVITY;
  for (uint32_t i = 0; i < FP_SAMPLES_PER_PHASE; i++) {
    const double va = (double)raw_adc1[i] * inv_fs * vref;
    const double vb = (double)raw_adc2[i] * inv_fs * vref;
    const double vc = (double)raw_adc3[i] * inv_fs * vref;
    phase_a[i] = (float)((va - (double)g_adc_offset_v[0]) / sens);
    phase_b[i] = (float)((vb - (double)g_adc_offset_v[1]) / sens);
    phase_c[i] = (float)((vc - (double)g_adc_offset_v[2]) / sens);
  }
}

void HAL_ADC_ConvCpltCallback(ADC_HandleTypeDef *hadc)
{
  if (hadc->Instance == ADC1) g_adc1_done = 1u;
  else if (hadc->Instance == ADC2) g_adc2_done = 1u;
  else if (hadc->Instance == ADC3) g_adc3_done = 1u;
  /* TIM2 must stop here: DMA is circular; otherwise the next trigger overwrites
   * this frame before the main loop runs (~1.64 s @ 5 kHz x 8192). */
  if (g_adc1_done && g_adc2_done && g_adc3_done) {
    HAL_TIM_Base_Stop(&htim2);
    g_all_ready = 1u;
  }
}

static void Reset_SampleFlags(void)
{
  g_adc1_done = 0u;
  g_adc2_done = 0u;
  g_adc3_done = 0u;
  g_all_ready = 0u;
}

/* ============================================================================
 * USB fault summary output (CDC TX race protected)
 * ============================================================================
 */

static void usb_tx_append_sn(char **pp, char *pe, const char *fmt, ...)
{
  char *p = *pp;
  size_t room = (pe > p) ? (size_t)(pe - p) : 0u;
  if (room < 2u) return;
  va_list ap; va_start(ap, fmt);
  int r = vsnprintf(p, room, fmt, ap);
  va_end(ap);
  if (r < 0) return;
  size_t w = (size_t)r; if (w >= room) w = room - 1u;
  *pp = p + w;
}

static void usb_tx_append_d(char **pp, char *pe, double x, unsigned dec)
{
  char *p = *pp;
  size_t room = (pe > p) ? (size_t)(pe - p) : 0u;
  if (room < 2u) return;
  size_t w = fmt_double(p, room, x, dec);
  if (w >= room) *pp = pe - 1; else *pp = p + w;
}

/* Dimensionless bearing coefficients (SET/GET/FULLREPORT [PARAMS] — not fault Hz). */
static void usb_tx_append_bearing_coeffs(char **pp, char *pe)
{
  usb_tx_append_sn(pp, pe, " BPFO="); usb_tx_append_d(pp, pe, (double)g_params.bpfo_coefficient, 4u);
  usb_tx_append_sn(pp, pe, " BPFI="); usb_tx_append_d(pp, pe, (double)g_params.bpfi_coefficient, 4u);
  usb_tx_append_sn(pp, pe, " BSF=");  usb_tx_append_d(pp, pe, (double)g_params.bsf_coefficient, 4u);
  usb_tx_append_sn(pp, pe, " FTF=");  usb_tx_append_d(pp, pe, (double)g_params.ftf_coefficient, 4u);
}

static void USB_Tx_WaitSend(uint16_t nbytes)
{
  uint32_t guard = 0u;
  while (CDC_Transmit_FS(usb_cdc_tx_buf, nbytes) != 0u && guard < 5000u) {
    HAL_Delay(1u);
    guard++;
  }
}

static void USB_Send_PhaseCsv(void)
{
  char *const p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;
  const float dt_s = 1.0f / FP_SAMPLE_RATE_HZ;

  if (!g_phase_csv_ready) {
    USB_Send_TextLine("ERR PHASECSV no_capture_yet\r\n");
    return;
  }

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "### H750_PHASE_CSV v1\r\n");
  usb_tx_append_sn(&p, pe, "FS_HZ=");
  usb_tx_append_d(&p, pe, (double)FP_SAMPLE_RATE_HZ, 3u);
  usb_tx_append_sn(&p, pe, " N=%u\r\n", (unsigned)FP_SAMPLES_PER_PHASE);
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[PHASE_A_CSV]\r\n");
  usb_tx_append_sn(&p, pe, "%s", "sample_idx,time_s,current_a\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));
  for (uint32_t i = 0u; i < FP_SAMPLES_PER_PHASE; i++) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%u,", (unsigned)i);
    usb_tx_append_d(&p, pe, (double)((float)i * dt_s), 7u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)g_phase_a_csv[i], 6u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[PHASE_B_CSV]\r\n");
  usb_tx_append_sn(&p, pe, "%s", "sample_idx,time_s,current_b\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));
  for (uint32_t i = 0u; i < FP_SAMPLES_PER_PHASE; i++) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%u,", (unsigned)i);
    usb_tx_append_d(&p, pe, (double)((float)i * dt_s), 7u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)g_phase_b_csv[i], 6u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[PHASE_C_CSV]\r\n");
  usb_tx_append_sn(&p, pe, "%s", "sample_idx,time_s,current_c\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));
  for (uint32_t i = 0u; i < FP_SAMPLES_PER_PHASE; i++) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%u,", (unsigned)i);
    usb_tx_append_d(&p, pe, (double)((float)i * dt_s), 7u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)g_phase_c_csv[i], 6u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "### END_PHASE_CSV\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));
}

/* Native one-sided magnitude spectrum (2048 bins, df=fs/N) for PC plotting. */
static void USB_ComputeOneSidedMag(const float *x, float *mag_half)
{
  if (FP_KURT_FFT_N != FP_FFT_SIZE) return;
  for (uint32_t n = 0u; n < FP_FFT_SIZE; n++) {
    fft_re[n] = x[n];
    fft_im[n] = 0.f;
  }
  FFT512_Core(fft_re, fft_im, 0);
  const uint32_t half = FP_FFT_SIZE / 2u;
  for (uint32_t k = 0u; k < half; k++) {
    const float pr = fft_re[k], pi = fft_im[k];
    mag_half[k] = sqrtf(pr * pr + pi * pi);
  }
}

static void USB_Send_NativeSpectrumCsv(const float *x, const char *section)
{
  char *const p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;
  static float mag_native[FP_HALF_FFT];
  const float df = FP_SAMPLE_RATE_HZ / (float)FP_FFT_SIZE;

  USB_ComputeOneSidedMag(x, mag_native);

  p = p0;
  usb_tx_append_sn(&p, pe, "### SECTION %s NATIVE_FFT N=%u DF_HZ=",
                    section, (unsigned)USB_NATIVE_SPEC_BINS);
  usb_tx_append_d(&p, pe, (double)df, 6u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "[%s_NATIVE_FFT_CSV]\r\n", section);
  usb_tx_append_sn(&p, pe, "%s", "bin,k,f_hz,magnitude\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  for (uint32_t k = 0u; k < USB_NATIVE_SPEC_BINS; k++) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%u,", (unsigned)k);
    usb_tx_append_d(&p, pe, (double)((float)k * df), 6u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)mag_native[k], 8u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_PhaseTimeNativeCsv(const float *sig, const char *name)
{
  char *const p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;
  const float dt_s = 1.0f / FP_SAMPLE_RATE_HZ;

  p = p0;
  usb_tx_append_sn(&p, pe, "### SECTION %s_TIME N=%u FS_HZ=", name, (unsigned)FP_SAMPLES_PER_PHASE);
  usb_tx_append_d(&p, pe, (double)FP_SAMPLE_RATE_HZ, 4u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "[%s_TIME_CSV]\r\n", name);
  usb_tx_append_sn(&p, pe, "%s", "sample_idx,time_s,amplitude\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  for (uint32_t i = 0u; i < FP_SAMPLES_PER_PHASE; i++) {
    if ((i % USB_GRAPH_PROGRESS_EVERY) == 0u && i > 0u) {
      p = p0;
      usb_tx_append_sn(&p, pe, "### PROGRESS %s_TIME %u/%u\r\n",
                        name, (unsigned)i, (unsigned)FP_SAMPLES_PER_PHASE);
      *p = '\0';
      USB_Tx_WaitSend((uint16_t)strlen(p0));
    }
    p = p0;
    usb_tx_append_sn(&p, pe, "%u,", (unsigned)i);
    usb_tx_append_d(&p, pe, (double)((float)i * dt_s), 8u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)sig[i], 8u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_PhaseAbcCombinedCsv(void)
{
  char *const p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;
  const float dt_s = 1.0f / FP_SAMPLE_RATE_HZ;
  const float *pa = g_phase_csv_ready ? g_phase_a_csv : phase_a;
  const float *pb = g_phase_csv_ready ? g_phase_b_csv : phase_b;
  const float *pc = g_phase_csv_ready ? g_phase_c_csv : phase_c;

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "### SECTION PHASE_ABC_COMBINED\r\n");
  usb_tx_append_sn(&p, pe, "N=%u FS_HZ=", (unsigned)FP_SAMPLES_PER_PHASE);
  usb_tx_append_d(&p, pe, (double)FP_SAMPLE_RATE_HZ, 4u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[PHASE_ABC_TIME_CSV]\r\n");
  usb_tx_append_sn(&p, pe, "%s", "sample_idx,time_s,current_a,current_b,current_c\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  for (uint32_t i = 0u; i < FP_SAMPLES_PER_PHASE; i++) {
    if ((i % USB_GRAPH_PROGRESS_EVERY) == 0u && i > 0u) {
      p = p0;
      usb_tx_append_sn(&p, pe, "### PROGRESS ABC %u/%u\r\n",
                        (unsigned)i, (unsigned)FP_SAMPLES_PER_PHASE);
      *p = '\0';
      USB_Tx_WaitSend((uint16_t)strlen(p0));
    }
    p = p0;
    usb_tx_append_sn(&p, pe, "%u,", (unsigned)i);
    usb_tx_append_d(&p, pe, (double)((float)i * dt_s), 8u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)pa[i], 8u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)pb[i], 8u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)pc[i], 8u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_SequenceTimeCsv(void)
{
  char *const p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;
  const float dt_s = 1.0f / FP_SAMPLE_RATE_HZ;
  const float a_r = -0.5f;
  const float a_i = 0.86602540378443864676f;
  const float a2_r = -0.5f;
  const float a2_i = -a_i;

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "### SECTION SEQUENCE_I012_TIME\r\n");
  usb_tx_append_sn(&p, pe, "%s", "[SEQ_I012_TIME_CSV]\r\n");
  usb_tx_append_sn(&p, pe, "%s", "sample_idx,time_s,i0,i1_mag,i2_mag\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  for (uint32_t n = 0u; n < FP_SAMPLES_PER_PHASE; n++) {
    const float ia = phase_a[n];
    const float ib = phase_b[n];
    const float ic = phase_c[n];
    const float i0 = (ia + ib + ic) / 3.0f;
    const float i1r = (ia + a_r * ib + a2_r * ic) / 3.0f;
    const float i1i = (a_i * ib + a2_i * ic) / 3.0f;
    const float i2r = (ia + a2_r * ib + a_r * ic) / 3.0f;
    const float i2i = (a2_i * ib + a_i * ic) / 3.0f;
    const float i1m = hypotf(i1r, i1i);
    const float i2m = hypotf(i2r, i2i);

    if ((n % USB_GRAPH_PROGRESS_EVERY) == 0u && n > 0u) {
      p = p0;
      usb_tx_append_sn(&p, pe, "### PROGRESS SEQ %u/%u\r\n",
                        (unsigned)n, (unsigned)FP_SAMPLES_PER_PHASE);
      *p = '\0';
      USB_Tx_WaitSend((uint16_t)strlen(p0));
    }
    p = p0;
    usb_tx_append_sn(&p, pe, "%u,", (unsigned)n);
    usb_tx_append_d(&p, pe, (double)((float)n * dt_s), 8u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)i0, 8u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)i1m, 8u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)i2m, 8u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_PhaseEnvelopeNativeCsv(const float *phase, const char *name)
{
  char *const p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;
  const float dt_s = 1.0f / FP_SAMPLE_RATE_HZ;
  uint32_t band = 0u;

  if (!g_kurt_coeffs_ready) { Kurtogram_InitCoeffs(); g_kurt_coeffs_ready = 1u; }
  for (uint32_t b = 0u; b < FP_KURT_NBANDS; b++) {
    if (kb_fc_hz[b] == g_fault.kurt_band_fc_hz) { band = b; break; }
  }
  Kurtogram_FilterTwoStage(band, phase, kurt_filt_buf, FP_FFT_SIZE);
  Hilbert_Envelope512(kurt_filt_buf, kurt_env_buf);

  p = p0;
  usb_tx_append_sn(&p, pe, "### SECTION %s_ENVELOPE KB_FC_HZ=", name);
  usb_tx_append_d(&p, pe, (double)g_fault.kurt_band_fc_hz, 1u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "[%s_ENVELOPE_CSV]\r\n", name);
  usb_tx_append_sn(&p, pe, "%s", "sample_idx,time_s,envelope\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  for (uint32_t i = 0u; i < FP_SAMPLES_PER_PHASE; i++) {
    if ((i % USB_GRAPH_PROGRESS_EVERY) == 0u && i > 0u) {
      p = p0;
      usb_tx_append_sn(&p, pe, "### PROGRESS %s_ENV %u/%u\r\n",
                        name, (unsigned)i, (unsigned)FP_SAMPLES_PER_PHASE);
      *p = '\0';
      USB_Tx_WaitSend((uint16_t)strlen(p0));
    }
    p = p0;
    usb_tx_append_sn(&p, pe, "%u,", (unsigned)i);
    usb_tx_append_d(&p, pe, (double)((float)i * dt_s), 8u);
    usb_tx_append_sn(&p, pe, ",");
    usb_tx_append_d(&p, pe, (double)kurt_env_buf[i], 8u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_ReportScalarsFull(void)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;
  float sk_max = 0.f, sk_f_hz = 0.f;

  for (uint32_t i = 0u; i < USB_EXPORT_GRID_BINS; i++) {
    if (g_usb_export_sk[i] > sk_max) {
      sk_max = g_usb_export_sk[i];
      sk_f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)USB_EXPORT_GRID_BINS;
    }
  }

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "### H750_FULL_REPORT v2\r\n");
  usb_tx_append_sn(&p, pe, "T_MS=%lu CAPTURE_READY=%u\r\n",
                    (unsigned long)HAL_GetTick(), (unsigned)g_phase_csv_ready);
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[PARAMS]");
  usb_tx_append_sn(&p, pe, " RPM="); usb_tx_append_d(&p, pe, (double)g_params.motor_rpm, 3u);
  usb_tx_append_sn(&p, pe, " SLIP="); usb_tx_append_d(&p, pe, (double)g_params.motor_slip, 4u);
  usb_tx_append_bearing_coeffs(&p, pe);
  usb_tx_append_sn(&p, pe, " LINE_HZ="); usb_tx_append_d(&p, pe, (double)g_supply_line_hz, 2u);
  usb_tx_append_sn(&p, pe, " FS_HZ="); usb_tx_append_d(&p, pe, (double)FP_SAMPLE_RATE_HZ, 4u);
  usb_tx_append_sn(&p, pe, " N="); usb_tx_append_d(&p, pe, (double)FP_FFT_SIZE, 0u);
  usb_tx_append_sn(&p, pe, " DF_HZ="); usb_tx_append_d(&p, pe, (double)(FP_SAMPLE_RATE_HZ / (float)FP_FFT_SIZE), 6u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[BEARING]");
  usb_tx_append_sn(&p, pe, " BPFO_HZ="); usb_tx_append_d(&p, pe, (double)g_fault.bpfo_hz, 3u);
  usb_tx_append_sn(&p, pe, " BPFI_HZ="); usb_tx_append_d(&p, pe, (double)g_fault.bpfi_hz, 3u);
  usb_tx_append_sn(&p, pe, " BSF_HZ="); usb_tx_append_d(&p, pe, (double)g_fault.bsf_hz, 3u);
  usb_tx_append_sn(&p, pe, " FTF_HZ="); usb_tx_append_d(&p, pe, (double)g_fault.ftf_hz, 3u);
  usb_tx_append_sn(&p, pe, " FI="); usb_tx_append_d(&p, pe, (double)g_fault.fault_index, 4u);
  usb_tx_append_sn(&p, pe, " FI_EMA="); usb_tx_append_d(&p, pe, (double)g_fault.fault_index_ema, 4u);
  usb_tx_append_sn(&p, pe, " CUSUM="); usb_tx_append_d(&p, pe, (double)g_fault.cusum_score, 4u);
  usb_tx_append_sn(&p, pe, " LV="); usb_tx_append_d(&p, pe, (double)g_fault.fault_level, 0u);
  usb_tx_append_sn(&p, pe, " FR_HZ="); usb_tx_append_d(&p, pe, (double)g_fault.shaft_hz_refined, 3u);
  usb_tx_append_sn(&p, pe, " SLIP="); usb_tx_append_d(&p, pe, (double)g_fault.slip_estimated, 4u);
  usb_tx_append_sn(&p, pe, " SLIP_OK="); usb_tx_append_d(&p, pe, (double)g_fault.slip_auto_valid, 0u);
  usb_tx_append_sn(&p, pe, " POLES="); usb_tx_append_d(&p, pe, (double)(2u * g_fault.pole_pairs), 0u);
  usb_tx_append_sn(&p, pe, " LS="); usb_tx_append_d(&p, pe, (double)g_fault.index_ls, 4u);
  usb_tx_append_sn(&p, pe, " FFT="); usb_tx_append_d(&p, pe, (double)g_fault.index_fft, 4u);
  usb_tx_append_sn(&p, pe, " CONF="); usb_tx_append_d(&p, pe, (double)g_fault.detect_confidence, 3u);
  usb_tx_append_sn(&p, pe, " MI="); usb_tx_append_d(&p, pe, (double)g_fault.index_music, 4u);
  usb_tx_append_sn(&p, pe, " ES="); usb_tx_append_d(&p, pe, (double)g_fault.index_esprit, 4u);
  usb_tx_append_sn(&p, pe, " TK="); usb_tx_append_d(&p, pe, (double)g_fault.index_teager, 4u);
  usb_tx_append_sn(&p, pe, " SK="); usb_tx_append_d(&p, pe, (double)g_fault.index_sk, 4u);
  usb_tx_append_sn(&p, pe, " WV="); usb_tx_append_d(&p, pe, (double)g_fault.index_wavelet, 4u);
  usb_tx_append_sn(&p, pe, " CY="); usb_tx_append_d(&p, pe, (double)g_fault.index_cyclic, 4u);
  usb_tx_append_sn(&p, pe, " SB="); usb_tx_append_d(&p, pe, (double)g_fault.index_sideband, 4u);
  usb_tx_append_sn(&p, pe, " ACF="); usb_tx_append_d(&p, pe, (double)g_fault.index_env_acf, 4u);
  usb_tx_append_sn(&p, pe, " WL="); usb_tx_append_d(&p, pe, (double)g_fault.index_welch, 4u);
  usb_tx_append_sn(&p, pe, " COH="); usb_tx_append_d(&p, pe, (double)g_fault.index_coherence, 4u);
  usb_tx_append_sn(&p, pe, " PF_O="); usb_tx_append_d(&p, pe, (double)g_fault.index_bpfo, 4u);
  usb_tx_append_sn(&p, pe, " PF_I="); usb_tx_append_d(&p, pe, (double)g_fault.index_bpfi, 4u);
  usb_tx_append_sn(&p, pe, " PF_B="); usb_tx_append_d(&p, pe, (double)g_fault.index_bsf, 4u);
  usb_tx_append_sn(&p, pe, " PF_T="); usb_tx_append_d(&p, pe, (double)g_fault.index_ftf, 4u);
  usb_tx_append_sn(&p, pe, " DOM="); usb_tx_append_d(&p, pe, (double)g_fault.dominant_fault, 0u);
  usb_tx_append_sn(&p, pe, " NP_F="); usb_tx_append_d(&p, pe, (double)g_fault.np_alarm_fourier, 0u);
  usb_tx_append_sn(&p, pe, " NP_M="); usb_tx_append_d(&p, pe, (double)g_fault.np_alarm_music, 0u);
  usb_tx_append_sn(&p, pe, " NP_E="); usb_tx_append_d(&p, pe, (double)g_fault.np_alarm_esprit, 0u);
  usb_tx_append_sn(&p, pe, " G_F="); usb_tx_append_d(&p, pe, (double)g_fault.np_gamma_fourier, 3u);
  usb_tx_append_sn(&p, pe, " G_M="); usb_tx_append_d(&p, pe, (double)g_fault.np_gamma_music, 3u);
  usb_tx_append_sn(&p, pe, " G_E="); usb_tx_append_d(&p, pe, (double)g_fault.np_gamma_esprit, 3u);
  usb_tx_append_sn(&p, pe, " SKPK="); usb_tx_append_d(&p, pe, (double)sk_max, 4u);
  usb_tx_append_sn(&p, pe, " SKPK_HZ="); usb_tx_append_d(&p, pe, (double)sk_f_hz, 2u);
  usb_tx_append_sn(&p, pe, " KB_HZ="); usb_tx_append_d(&p, pe, (double)g_fault.kurt_band_fc_hz, 0u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[STATOR]");
  usb_tx_append_sn(&p, pe, " SH="); usb_tx_append_d(&p, pe, (double)g_fault.stator_fault_short, 0u);
  usb_tx_append_sn(&p, pe, " GD="); usb_tx_append_d(&p, pe, (double)g_fault.stator_fault_gnd, 0u);
  usb_tx_append_sn(&p, pe, " ES="); usb_tx_append_d(&p, pe, (double)g_fault.stator_early_short, 0u);
  usb_tx_append_sn(&p, pe, " EG="); usb_tx_append_d(&p, pe, (double)g_fault.stator_early_gnd, 0u);
  usb_tx_append_sn(&p, pe, " SLV="); usb_tx_append_d(&p, pe, (double)g_fault.stator_fault_level, 0u);
  usb_tx_append_sn(&p, pe, " NSR="); usb_tx_append_d(&p, pe, (double)g_fault.stator_nsr, 5u);
  usb_tx_append_sn(&p, pe, " ZSR="); usb_tx_append_d(&p, pe, (double)g_fault.stator_zsr, 5u);
  usb_tx_append_sn(&p, pe, " NSR_TD="); usb_tx_append_d(&p, pe, (double)g_fault.stator_nsr_td, 5u);
  usb_tx_append_sn(&p, pe, " ZSR_TD="); usb_tx_append_d(&p, pe, (double)g_fault.stator_zsr_td, 5u);
  usb_tx_append_sn(&p, pe, " NSR_H5="); usb_tx_append_d(&p, pe, (double)g_fault.stator_nsr_h5, 5u);
  usb_tx_append_sn(&p, pe, " ZSR_H3="); usb_tx_append_d(&p, pe, (double)g_fault.stator_zsr_h3, 5u);
  usb_tx_append_sn(&p, pe, " IMB_PCT="); usb_tx_append_d(&p, pe, (double)g_fault.stator_imbalance_pct, 3u);
  usb_tx_append_sn(&p, pe, " HARM="); usb_tx_append_d(&p, pe, (double)g_fault.stator_harm_ratio, 5u);
  usb_tx_append_sn(&p, pe, " ODD="); usb_tx_append_d(&p, pe, (double)g_fault.stator_odd_harm_index, 5u);
  usb_tx_append_sn(&p, pe, " PS_DEG="); usb_tx_append_d(&p, pe, (double)g_fault.stator_phase_spread_deg, 3u);
  usb_tx_append_sn(&p, pe, " RESID="); usb_tx_append_d(&p, pe, (double)g_fault.stator_resid_gnd_ratio, 5u);
  usb_tx_append_sn(&p, pe, " SI="); usb_tx_append_d(&p, pe, (double)g_fault.stator_short_index, 4u);
  usb_tx_append_sn(&p, pe, " GI="); usb_tx_append_d(&p, pe, (double)g_fault.stator_gnd_index, 4u);
  usb_tx_append_sn(&p, pe, " SI_EMA="); usb_tx_append_d(&p, pe, (double)g_fault.stator_short_index_ema, 4u);
  usb_tx_append_sn(&p, pe, " GI_EMA="); usb_tx_append_d(&p, pe, (double)g_fault.stator_gnd_index_ema, 4u);
  usb_tx_append_sn(&p, pe, " CS="); usb_tx_append_d(&p, pe, (double)g_fault.stator_cusum_short, 4u);
  usb_tx_append_sn(&p, pe, " CG="); usb_tx_append_d(&p, pe, (double)g_fault.stator_cusum_gnd, 4u);
  usb_tx_append_sn(&p, pe, " I0="); usb_tx_append_d(&p, pe, (double)g_fault.stator_i0_mag, 6u);
  usb_tx_append_sn(&p, pe, " I1="); usb_tx_append_d(&p, pe, (double)g_fault.stator_i1_mag, 6u);
  usb_tx_append_sn(&p, pe, " I2="); usb_tx_append_d(&p, pe, (double)g_fault.stator_i2_mag, 6u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));
}

static void USB_Send_GraphDataPack(void)
{
  if (!g_phase_csv_ready && g_all_ready == 0u) {
    USB_Send_TextLine("ERR GRAPHDATA wait for one capture cycle\r\n");
    return;
  }

  USB_Send_TextLine("### BEGIN_GRAPHDATA\r\n");

  USB_Send_PhaseAbcCombinedCsv();
  USB_Send_SequenceTimeCsv();

  USB_Send_PhaseTimeNativeCsv(phase_a, "PHASE_A");
  USB_Send_PhaseTimeNativeCsv(phase_b, "PHASE_B");
  USB_Send_PhaseTimeNativeCsv(phase_c, "PHASE_C");

  USB_Send_PhaseEnvelopeNativeCsv(phase_a, "PHASE_A");
  USB_Send_PhaseEnvelopeNativeCsv(phase_b, "PHASE_B");
  USB_Send_PhaseEnvelopeNativeCsv(phase_c, "PHASE_C");

  USB_Send_NativeSpectrumCsv(phase_a, "PHASE_A");
  USB_Send_NativeSpectrumCsv(phase_b, "PHASE_B");
  USB_Send_NativeSpectrumCsv(phase_c, "PHASE_C");
  for (uint32_t i = 0u; i < FP_FFT_SIZE; i++)
    wav_buf_a[i] = (phase_a[i] + phase_b[i] + phase_c[i]) / 3.0f;
  USB_Send_NativeSpectrumCsv(wav_buf_a, "MEAN_3PH");

  USB_Send_TextLine("### END_GRAPHDATA\r\n");
}

static void USB_Send_FullReport(void)
{
  if (!g_phase_csv_ready) {
    USB_Send_TextLine("ERR FULLREPORT wait for capture (one frame ~1.64s)\r\n");
    return;
  }

  USB_Send_ReportScalarsFull();
#if USB_FULLREPORT_INCLUDE_SPECTRAL
  USB_Send_SpectralFigures();
#endif
  USB_Send_GraphDataPack();
  USB_Send_TextLine("### END_FULL_REPORT\r\n");
}

/* Plot-ready blocks: meta, ESPRIT peaks, MUSIC eigenvalues, Fourier + MUSIC uniform f-grids */
static void USB_Send_Spectral_MetaAndModelIdx(const char *mode)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  p = p0;
  usb_tx_append_sn(&p, pe, "### H750_DSP v1 MODE=%s T_MS=%lu\r\n",
                    mode, (unsigned long)HAL_GetTick());
  usb_tx_append_sn(&p, pe, "%s", "[META]");
  usb_tx_append_sn(&p, pe, " FS_HZ="); usb_tx_append_d(&p, pe, (double)FP_SAMPLE_RATE_HZ, 4u);
  usb_tx_append_sn(&p, pe, " N=%u", (unsigned)FP_FFT_SIZE);
  usb_tx_append_sn(&p, pe, " L=%u", (unsigned)FP_SUB_L);
  usb_tx_append_sn(&p, pe, " M=%u", (unsigned)FP_SUB_M);
  usb_tx_append_sn(&p, pe, " DF_HZ="); usb_tx_append_d(&p, pe, (double)(FP_SAMPLE_RATE_HZ / (float)FP_FFT_SIZE), 6u);
  usb_tx_append_sn(&p, pe, " LINE_HZ="); usb_tx_append_d(&p, pe, (double)g_supply_line_hz, 2u);
  usb_tx_append_sn(&p, pe, " H=%u", (unsigned)g_params.harmonic_orders);
  usb_tx_append_sn(&p, pe, " BW_HZ="); usb_tx_append_d(&p, pe, (double)g_params.bandwidth_hz, 2u);
  usb_tx_append_sn(&p, pe, " SPEC_BINS=%u NATIVE_BINS=%u\r\n",
                    (unsigned)USB_EXPORT_GRID_BINS, (unsigned)USB_EXPORT_NATIVE_BINS);
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[MODEL_IDX] FI=");
  usb_tx_append_d(&p, pe, (double)g_fault.fault_index, 4u);
  usb_tx_append_sn(&p, pe, " FR_HZ="); usb_tx_append_d(&p, pe, (double)g_fault.shaft_hz_refined, 3u);
  usb_tx_append_sn(&p, pe, " SLIP="); usb_tx_append_d(&p, pe, (double)g_fault.slip_estimated, 4u);
  usb_tx_append_sn(&p, pe, " SLIP_OK="); usb_tx_append_d(&p, pe, (double)g_fault.slip_auto_valid, 0u);
  usb_tx_append_sn(&p, pe, " LS="); usb_tx_append_d(&p, pe, (double)g_fault.index_ls, 4u);
  usb_tx_append_sn(&p, pe, " FFT="); usb_tx_append_d(&p, pe, (double)g_fault.index_fft, 4u);
  usb_tx_append_sn(&p, pe, " CONF="); usb_tx_append_d(&p, pe, (double)g_fault.detect_confidence, 3u);
  usb_tx_append_sn(&p, pe, " MI="); usb_tx_append_d(&p, pe, (double)g_fault.index_music, 4u);
  usb_tx_append_sn(&p, pe, " ES="); usb_tx_append_d(&p, pe, (double)g_fault.index_esprit, 4u);
  usb_tx_append_sn(&p, pe, " TK="); usb_tx_append_d(&p, pe, (double)g_fault.index_teager, 4u);
  usb_tx_append_sn(&p, pe, " SK="); usb_tx_append_d(&p, pe, (double)g_fault.index_sk, 4u);
  usb_tx_append_sn(&p, pe, " WV="); usb_tx_append_d(&p, pe, (double)g_fault.index_wavelet, 4u);
  usb_tx_append_sn(&p, pe, " CY="); usb_tx_append_d(&p, pe, (double)g_fault.index_cyclic, 4u);
  usb_tx_append_sn(&p, pe, " SB="); usb_tx_append_d(&p, pe, (double)g_fault.index_sideband, 4u);
  usb_tx_append_sn(&p, pe, " ACF="); usb_tx_append_d(&p, pe, (double)g_fault.index_env_acf, 4u);
  usb_tx_append_sn(&p, pe, " WL="); usb_tx_append_d(&p, pe, (double)g_fault.index_welch, 4u);
  usb_tx_append_sn(&p, pe, " COH="); usb_tx_append_d(&p, pe, (double)g_fault.index_coherence, 4u);
  usb_tx_append_sn(&p, pe, " MU_OK=%u EP_OK=%u\r\n",
                    (unsigned)g_fault.exp_music_ok, (unsigned)g_fault.exp_esprit_ok);
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));
}

static void USB_Send_Spectral_Section_Esprit(void)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[ESPRIT_HZ] N=");
  usb_tx_append_sn(&p, pe, "%u", (unsigned)g_fault.exp_esprit_n);
  for (uint32_t i = 0u; i < (uint32_t)g_fault.exp_esprit_n && i < 16u; i++) {
    usb_tx_append_sn(&p, pe, " ");
    usb_tx_append_d(&p, pe, (double)g_fault.exp_esprit_f_hz[i], 3u);
  }
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[ESPRIT_CSV] OK=");
  usb_tx_append_sn(&p, pe, "%u", (unsigned)g_fault.exp_esprit_ok);
  usb_tx_append_sn(&p, pe, ";N=");
  usb_tx_append_sn(&p, pe, "%u", (unsigned)g_fault.exp_esprit_n);
  usb_tx_append_sn(&p, pe, ";ES_IDX=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_esprit, 4u);
  if (g_fault.exp_esprit_n > 0u) {
    for (uint32_t i = 0u; i < (uint32_t)g_fault.exp_esprit_n && i < 16u; i++) {
      usb_tx_append_sn(&p, pe, "%s", ";");
      usb_tx_append_sn(&p, pe, "%u", (unsigned)i);
      usb_tx_append_sn(&p, pe, ",");
      usb_tx_append_d(&p, pe, (double)g_fault.exp_esprit_f_hz[i], 3u);
    }
  }
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  if (g_fault.exp_esprit_n > 0u) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%s", "[ESPRIT_TSV] ");
    for (uint32_t i = 0u; i < (uint32_t)g_fault.exp_esprit_n && i < 16u; i++) {
      if (i > 0u) usb_tx_append_sn(&p, pe, "\t");
      usb_tx_append_d(&p, pe, (double)g_fault.exp_esprit_f_hz[i], 3u);
    }
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_Spectral_Section_MusicEval(void)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  for (uint32_t row = 0u; row < FP_SUB_L; row += 8u) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%s", "[MUSIC_EVAL_DESC]");
    for (uint32_t j = 0u; j < 8u && row + j < FP_SUB_L; j++) {
      usb_tx_append_sn(&p, pe, " ");
      usb_tx_append_d(&p, pe, (double)g_fault.exp_music_eval[row + j], 5u);
    }
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_Spectral_Section_FourierCsv(void)
{
#if FP_HIGH_ACCURACY_EN
  USB_Send_CsvSpectrumRowsFromHalf("[FOURIER_CSV]", g_fft_mag_half, FP_HALF_FFT,
                                   USB_EXPORT_NATIVE_BINS, 0u);
#else
  USB_Send_CsvSpectrumRows("[FOURIER_CSV]", g_usb_export_fft);
#endif
}

static void USB_Send_Spectral_Section_MusicCsv(void)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  for (uint32_t base = 0u; base < USB_EXPORT_GRID_BINS; base += USB_EXPORT_CSV_ROW_PAIRS) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%s", "[MUSIC_CSV] ");
    for (uint32_t j = 0u; j < USB_EXPORT_CSV_ROW_PAIRS && base + j < USB_EXPORT_GRID_BINS; j++) {
      const uint32_t i = base + j;
      const float f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)USB_EXPORT_GRID_BINS;
      if (j > 0u) usb_tx_append_sn(&p, pe, "%s", ";");
      usb_tx_append_d(&p, pe, (double)f_hz, 3u);
      usb_tx_append_sn(&p, pe, ",");
      usb_tx_append_d(&p, pe, (double)g_usb_export_music[i], 6u);
    }
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

/* Per-phase export buffers (PC app: [FOURIER_P1_CSV] / [MUSIC_P1_CSV] three-color overlay). */
static float RAM_D2_BULK g_usb_export_fft_p1[USB_EXPORT_GRID_BINS];
static float RAM_D2_BULK g_usb_export_fft_p2[USB_EXPORT_GRID_BINS];
static float RAM_D2_BULK g_usb_export_fft_p3[USB_EXPORT_GRID_BINS];
static float RAM_D2_BULK g_usb_export_music_p1[USB_EXPORT_GRID_BINS];
static float RAM_D2_BULK g_usb_export_music_p2[USB_EXPORT_GRID_BINS];
static float RAM_D2_BULK g_usb_export_music_p3[USB_EXPORT_GRID_BINS];

static void Phase_CopyCleanResidual(const float *phase_in, float *phase_out)
{
  for (uint32_t n = 0u; n < FP_FFT_SIZE; n++)
    phase_out[n] = phase_in[n];
  for (uint32_t h = 1u; h <= FP_SUPPLY_HARMONICS_REMOVE; h++)
    LS_Subtract_Line(phase_out, FP_FFT_SIZE, g_supply_line_hz * (float)h, FP_SAMPLE_RATE_HZ);
}

static void FillMusicExportBins_FromPhase(const float *phase_win, float *out_bins)
{
  float evals[32];
  uint32_t i;

  if (FP_FFT_SIZE < FP_SUB_L) {
    for (i = 0u; i < USB_EXPORT_GRID_BINS; i++) out_bins[i] = 0.f;
    return;
  }

  Phase_CopyCleanResidual(phase_win, wav_buf_a);
  Toeplitz_Autocorr(wav_buf_a, FP_FFT_SIZE, FP_SUB_L, sub_R);
  memcpy(sub_A, sub_R, sizeof(sub_R));
  Jacobi_EigenSym(FP_SUB_L, sub_A, sub_V);
  for (i = 0u; i < FP_SUB_L; i++) evals[i] = sub_A[i * FP_SUB_L + i];
  SortEigenpairsDesc(FP_SUB_L, evals, sub_V);

  for (i = 0u; i < USB_EXPORT_GRID_BINS; i++) {
    const float f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)USB_EXPORT_GRID_BINS;
    out_bins[i] = MUSIC_P_AtF(sub_V, FP_SUB_L, FP_SUB_M, f_hz, FP_SAMPLE_RATE_HZ);
  }
}

static void USB_Send_CsvSpectrumRows(const char *line_tag, const float *bins)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  for (uint32_t base = 0u; base < USB_EXPORT_GRID_BINS; base += USB_EXPORT_CSV_ROW_PAIRS) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%s", line_tag);
    usb_tx_append_sn(&p, pe, "%s", " ");
    for (uint32_t j = 0u; j < USB_EXPORT_CSV_ROW_PAIRS && base + j < USB_EXPORT_GRID_BINS; j++) {
      const uint32_t i = base + j;
      const float f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)USB_EXPORT_GRID_BINS;
      if (j > 0u) usb_tx_append_sn(&p, pe, "%s", ";");
      usb_tx_append_d(&p, pe, (double)f_hz, 3u);
      usb_tx_append_sn(&p, pe, ",");
      usb_tx_append_d(&p, pe, (double)bins[i], 6u);
    }
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static float ExportHalf_BinValue(const float *half, uint32_t half_n, uint32_t bin,
                                 uint32_t nbins, uint8_t sqrt_psd)
{
  uint32_t k0 = (bin * half_n) / nbins;
  uint32_t k1 = ((bin + 1u) * half_n) / nbins;
  if (k1 <= k0) k1 = k0 + 1u;
  if (k1 > half_n) k1 = half_n;
  float m = 0.f;
  for (uint32_t k = k0; k < k1; k++) {
    const float t = half[k];
    if (t > m) m = t;
  }
  return sqrt_psd ? sqrtf(m) : m;
}

static void USB_Send_CsvSpectrumRowsFromHalf(const char *line_tag, const float *half,
                                             uint32_t half_n, uint32_t nbins, uint8_t sqrt_psd)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  for (uint32_t base = 0u; base < nbins; base += USB_EXPORT_CSV_ROW_PAIRS) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%s", line_tag);
    usb_tx_append_sn(&p, pe, "%s", " ");
    for (uint32_t j = 0u; j < USB_EXPORT_CSV_ROW_PAIRS && base + j < nbins; j++) {
      const uint32_t i = base + j;
      const float f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)nbins;
      const float val = ExportHalf_BinValue(half, half_n, i, nbins, sqrt_psd);
      if (j > 0u) usb_tx_append_sn(&p, pe, "%s", ";");
      usb_tx_append_d(&p, pe, (double)f_hz, 3u);
      usb_tx_append_sn(&p, pe, ",");
      usb_tx_append_d(&p, pe, (double)val, 6u);
    }
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_Spectral_Section_PhaseOverlays(void)
{
  FillExportFftMag_bins(phase_a, FP_FFT_SIZE, fft_re, fft_im,
                        g_usb_export_fft_p1, USB_EXPORT_GRID_BINS);
  FillExportFftMag_bins(phase_b, FP_FFT_SIZE, fft_re, fft_im,
                        g_usb_export_fft_p2, USB_EXPORT_GRID_BINS);
  FillExportFftMag_bins(phase_c, FP_FFT_SIZE, fft_re, fft_im,
                        g_usb_export_fft_p3, USB_EXPORT_GRID_BINS);

  USB_Send_CsvSpectrumRows("[FOURIER_P1_CSV]", g_usb_export_fft_p1);
  USB_Send_CsvSpectrumRows("[FOURIER_P2_CSV]", g_usb_export_fft_p2);
  USB_Send_CsvSpectrumRows("[FOURIER_P3_CSV]", g_usb_export_fft_p3);

  if (g_fault.exp_music_ok) {
    FillMusicExportBins_FromPhase(phase_a, g_usb_export_music_p1);
    FillMusicExportBins_FromPhase(phase_b, g_usb_export_music_p2);
    FillMusicExportBins_FromPhase(phase_c, g_usb_export_music_p3);
    USB_Send_CsvSpectrumRows("[MUSIC_P1_CSV]", g_usb_export_music_p1);
    USB_Send_CsvSpectrumRows("[MUSIC_P2_CSV]", g_usb_export_music_p2);
    USB_Send_CsvSpectrumRows("[MUSIC_P3_CSV]", g_usb_export_music_p3);
  }
}

static void USB_Send_Spectral_Section_Cyclic2Csv(void)
{
#if FP_HIGH_ACCURACY_EN
  USB_Send_CsvSpectrumRowsFromHalf("[CYCLIC2_CSV]", g_cyclic2_psq, FP_HALF_FFT,
                                   USB_EXPORT_NATIVE_BINS, 1u);
#else
  USB_Send_CsvSpectrumRows("[CYCLIC2_CSV]", g_usb_export_cyclic2);
#endif
}

static void USB_Send_Spectral_Section_SkCsv(void)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  for (uint32_t base = 0u; base < USB_EXPORT_GRID_BINS; base += USB_EXPORT_CSV_ROW_PAIRS) {
    p = p0;
    usb_tx_append_sn(&p, pe, "%s", "[SK_CSV] ");
    for (uint32_t j = 0u; j < USB_EXPORT_CSV_ROW_PAIRS && base + j < USB_EXPORT_GRID_BINS; j++) {
      const uint32_t i = base + j;
      const float f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)USB_EXPORT_GRID_BINS;
      if (j > 0u) usb_tx_append_sn(&p, pe, "%s", ";");
      usb_tx_append_d(&p, pe, (double)f_hz, 3u);
      usb_tx_append_sn(&p, pe, ",");
      usb_tx_append_d(&p, pe, (double)g_usb_export_sk[i], 6u);
    }
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_Spectral_Section_WelchCsv(void)
{
  USB_Send_CsvSpectrumRowsFromHalf("[WELCH_CSV]", g_welch_psd_half, FP_HALF_FFT,
                                   USB_EXPORT_NATIVE_BINS, 1u);
}

static void USB_Send_Spectral_Section_CohCsv(void)
{
  USB_Send_CsvSpectrumRowsFromHalf("[COH_CSV]", g_coh_min_half, FP_HALF_FFT,
                                   USB_EXPORT_NATIVE_BINS, 0u);
}

static void USB_Send_Spectral_Section_Wavelet(void)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "[WAVELET_META] DB4 DWT LEV=0..4\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));

  for (uint32_t lev = 0u; lev < FP_WAVELET_LEVELS; lev++) {
    const float f_nom = FP_SAMPLE_RATE_HZ / (float)(4u << lev);
    const float em = (g_fault.exp_wavelet_Ea[lev] + g_fault.exp_wavelet_Eb[lev] + g_fault.exp_wavelet_Ec[lev]) / 3.0f;
    p = p0;
    usb_tx_append_sn(&p, pe, "[WAVELET_CSV] LEV=%u F_HZ=", (unsigned)lev);
    usb_tx_append_d(&p, pe, (double)f_nom, 3u);
    usb_tx_append_sn(&p, pe, " EA="); usb_tx_append_d(&p, pe, (double)g_fault.exp_wavelet_Ea[lev], 7u);
    usb_tx_append_sn(&p, pe, " EB="); usb_tx_append_d(&p, pe, (double)g_fault.exp_wavelet_Eb[lev], 7u);
    usb_tx_append_sn(&p, pe, " EC="); usb_tx_append_d(&p, pe, (double)g_fault.exp_wavelet_Ec[lev], 7u);
    usb_tx_append_sn(&p, pe, " EMEAN="); usb_tx_append_d(&p, pe, (double)em, 7u);
    usb_tx_append_sn(&p, pe, "%s", "\r\n");
    *p = '\0';
    USB_Tx_WaitSend((uint16_t)strlen(p0));
  }
}

static void USB_Send_SpectralFigures(void)
{
  char *p0 = (char *)usb_cdc_tx_buf;
  char *const pe = p0 + sizeof(usb_cdc_tx_buf);
  char *p = p0;

  USB_Send_Spectral_MetaAndModelIdx("FULL");
  USB_Send_Spectral_Section_Esprit();
  USB_Send_Spectral_Section_MusicEval();
  USB_Send_Spectral_Section_FourierCsv();
  USB_Send_Spectral_Section_MusicCsv();
  USB_Send_Spectral_Section_PhaseOverlays();
  USB_Send_Spectral_Section_Cyclic2Csv();
  USB_Send_Spectral_Section_SkCsv();
  USB_Send_Spectral_Section_WelchCsv();
  USB_Send_Spectral_Section_CohCsv();
  USB_Send_Spectral_Section_Wavelet();

  p = p0;
  usb_tx_append_sn(&p, pe, "%s", "### END_REPORT\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen(p0));
}

/* CRC-16/CCITT (polynomial 0x1021, init 0xFFFF) for binary frame integrity. */
static uint16_t CRC16_CCITT(const uint8_t *data, uint32_t len)
{
  uint16_t crc = 0xFFFFu;
  for (uint32_t i = 0u; i < len; i++)
  {
    crc ^= (uint16_t)((uint16_t)data[i] << 8u);
    for (uint32_t j = 0u; j < 8u; j++)
    {
      if (crc & 0x8000u)
        crc = (uint16_t)((crc << 1u) ^ 0x1021u);
      else
        crc = (uint16_t)(crc << 1u);
    }
  }
  return crc;
}

/* ============================================================================
 * Binary fault frame sender — called instead of USB_Send_FaultSummary when
 * g_usb_binary_stream == 1.  Sends a compact packed struct framed by
 * [0xAA 0x55 CMD] ... [CRC16] [0x55 0xAA] for fast live monitoring.
 * CSV exports (FFTCSV etc.) are unaffected — always text.
 * ============================================================================ */
static void USB_Send_FaultBinaryFrame(void)
{
  FaultBinaryFrame_t frame;
  (void)memset(&frame, 0, sizeof(frame));

  /* ---- Header ---- */
  frame.frame_id        = g_usb_frame_counter++;
  frame.timestamp_ms    = HAL_GetTick();
  frame.rpm             = g_params.motor_rpm;
  frame.slip            = g_params.motor_slip;
  frame.shaft_hz        = g_fault.shaft_hz_refined;
  frame.supply_line_hz  = g_supply_line_hz;

  /* ---- Shaft / slip tracking ---- */
  frame.slip_estimated  = g_fault.slip_estimated;
  frame.slip_auto_valid = g_fault.slip_auto_valid;
  frame.pole_pairs      = g_fault.pole_pairs;

  /* ---- Bearing fault frequencies (Hz) ---- */
  frame.bpfo_hz         = g_fault.bpfo_hz;
  frame.bpfi_hz         = g_fault.bpfi_hz;
  frame.bsf_hz          = g_fault.bsf_hz;
  frame.ftf_hz          = g_fault.ftf_hz;

  /* ---- 12 MCSA detection method indices ---- */
  frame.index_ls        = g_fault.index_ls;
  frame.index_fft       = g_fault.index_fft;
  frame.index_music     = g_fault.index_music;
  frame.index_esprit    = g_fault.index_esprit;
  frame.index_teager    = g_fault.index_teager;
  frame.index_sk        = g_fault.index_sk;
  frame.index_wavelet   = g_fault.index_wavelet;
  frame.index_cyclic    = g_fault.index_cyclic;
  frame.index_sideband  = g_fault.index_sideband;
  frame.index_env_acf   = g_fault.index_env_acf;
  frame.index_welch     = g_fault.index_welch;
  frame.index_coherence = g_fault.index_coherence;

  /* ---- Spectral kurtosis peak (matches SKPK in text summary) ---- */
  {
    float sk_max = 0.0f;
    float sk_f_hz = 0.0f;
    for (uint32_t i = 0u; i < USB_EXPORT_GRID_BINS; i++) {
      const float v = g_usb_export_sk[i];
      if (v > sk_max) {
        sk_max = v;
        sk_f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)USB_EXPORT_GRID_BINS;
      }
    }
    frame.sk_peak_kurtosis = sk_max;
    frame.sk_peak_f_hz     = sk_f_hz;
  }
  frame.kurt_band_fc_hz = g_fault.kurt_band_fc_hz;

  /* ---- 4 Bearing partial-fault indices ---- */
  frame.index_bpfo      = g_fault.index_bpfo;
  frame.index_bpfi      = g_fault.index_bpfi;
  frame.index_bsf       = g_fault.index_bsf;
  frame.index_ftf       = g_fault.index_ftf;

  /* ---- Fused bearing scores ---- */
  frame.fault_index     = g_fault.fault_index;
  frame.fault_index_ema = g_fault.fault_index_ema;
  frame.cusum_score     = g_fault.cusum_score;
  frame.detect_confidence = g_fault.detect_confidence;
  frame.fault_level     = g_fault.fault_level;
  frame.dominant_fault  = g_fault.dominant_fault;
  frame.np_alarm_votes  = g_fault.np_alarm_fourier + g_fault.np_alarm_music + g_fault.np_alarm_esprit;
  frame.np_alarm_fourier  = g_fault.np_alarm_fourier;
  frame.np_alarm_music    = g_fault.np_alarm_music;
  frame.np_alarm_esprit   = g_fault.np_alarm_esprit;

  /* ---- NP (Neyman-Pearson) amplitudes ---- */
  frame.amp_fourier_fault = g_fault.amp_fourier_fault;
  frame.amp_fourier_base  = g_fault.amp_fourier_base;
  frame.amp_music_fault   = g_fault.amp_music_fault;
  frame.amp_music_base    = g_fault.amp_music_base;
  frame.amp_esprit_fault  = g_fault.amp_esprit_fault;
  frame.amp_esprit_base   = g_fault.amp_esprit_base;

  /* ---- NP F-test critical ratios ---- */
  frame.np_gamma_fourier = g_fault.np_gamma_fourier;
  frame.np_gamma_music   = g_fault.np_gamma_music;
  frame.np_gamma_esprit  = g_fault.np_gamma_esprit;

  /* ---- Stator winding metrics — spectral symmetrical components ---- */
  frame.stator_i0_mag   = g_fault.stator_i0_mag;
  frame.stator_i1_mag   = g_fault.stator_i1_mag;
  frame.stator_i2_mag   = g_fault.stator_i2_mag;
  frame.stator_nsr      = g_fault.stator_nsr;
  frame.stator_zsr      = g_fault.stator_zsr;
  frame.stator_imbalance_pct = g_fault.stator_imbalance_pct;
  frame.stator_harm_ratio = g_fault.stator_harm_ratio;
  frame.stator_resid_gnd_ratio = g_fault.stator_resid_gnd_ratio;
  frame.stator_zsr_h3   = g_fault.stator_zsr_h3;
  frame.stator_nsr_h5   = g_fault.stator_nsr_h5;

  /* ---- Stator winding metrics — time-domain symmetrical components ---- */
  frame.stator_i0_rms_td = g_fault.stator_i0_rms_td;
  frame.stator_i1_rms_td = g_fault.stator_i1_rms_td;
  frame.stator_i2_rms_td = g_fault.stator_i2_rms_td;
  frame.stator_nsr_td   = g_fault.stator_nsr_td;
  frame.stator_zsr_td   = g_fault.stator_zsr_td;

  /* ---- Stator winding metrics — composite indices ---- */
  frame.stator_odd_harm_index = g_fault.stator_odd_harm_index;
  frame.stator_phase_spread_deg = g_fault.stator_phase_spread_deg;
  frame.stator_short_index = g_fault.stator_short_index;
  frame.stator_gnd_index = g_fault.stator_gnd_index;
  frame.stator_short_index_ema = g_fault.stator_short_index_ema;
  frame.stator_gnd_index_ema = g_fault.stator_gnd_index_ema;
  frame.stator_cusum_short = g_fault.stator_cusum_short;
  frame.stator_cusum_gnd = g_fault.stator_cusum_gnd;

  /* ---- Stator alarm / incipient flags ---- */
  frame.stator_fault_level = g_fault.stator_fault_level;
  frame.stator_fault_short = g_fault.stator_fault_short;
  frame.stator_fault_gnd   = g_fault.stator_fault_gnd;
  frame.stator_early_short = g_fault.stator_early_short;
  frame.stator_early_gnd   = g_fault.stator_early_gnd;

  /* Compute CRC over everything before the crc16 field */
  frame.crc16 = CRC16_CCITT((const uint8_t *)&frame,
                              sizeof(frame) - sizeof(uint16_t));

  /* Assemble wire packet: [0xAA][0x55][0xF0][frame...][0x55][0xAA] */
  uint16_t idx = 0u;
  usb_cdc_tx_buf[idx++] = 0xAAu;
  usb_cdc_tx_buf[idx++] = 0x55u;
  usb_cdc_tx_buf[idx++] = CMD_FAULT_BINARY_FRAME;
  (void)memcpy(&usb_cdc_tx_buf[idx], &frame, sizeof(frame));
  idx += (uint16_t)sizeof(frame);
  usb_cdc_tx_buf[idx++] = 0x55u;
  usb_cdc_tx_buf[idx++] = 0xAAu;
  USB_Tx_WaitSend(idx);
}

static void USB_Send_FaultSummary(void)
{
  char *p;
  char *const pe = (char *)usb_cdc_tx_buf + sizeof(usb_cdc_tx_buf);
  float sk_max = 0.0f;
  float sk_f_hz = 0.0f;

  for (uint32_t i = 0u; i < USB_EXPORT_GRID_BINS; i++) {
    const float v = g_usb_export_sk[i];
    if (v > sk_max) {
      sk_max = v;
      sk_f_hz = ((float)i + 0.5f) * (FP_SAMPLE_RATE_HZ * 0.5f) / (float)USB_EXPORT_GRID_BINS;
    }
  }

  /* ---- Line 1: Bearing fault frequencies (Hz) — not SET/GET coefficients ---- */
  p = (char *)usb_cdc_tx_buf;
  usb_tx_append_sn(&p, pe, "%s", "BPFO_HZ=");
  usb_tx_append_d(&p, pe, (double)g_fault.bpfo_hz, 2u);
  usb_tx_append_sn(&p, pe, "%s", " BPFI_HZ=");
  usb_tx_append_d(&p, pe, (double)g_fault.bpfi_hz, 2u);
  usb_tx_append_sn(&p, pe, "%s", " BSF_HZ=");
  usb_tx_append_d(&p, pe, (double)g_fault.bsf_hz, 2u);
  usb_tx_append_sn(&p, pe, "%s", " FTF_HZ=");
  usb_tx_append_d(&p, pe, (double)g_fault.ftf_hz, 2u);
  usb_tx_append_sn(&p, pe, "%s", " FI=");
  usb_tx_append_d(&p, pe, (double)g_fault.fault_index, 3u);
  usb_tx_append_sn(&p, pe, "%s", " FR=");
  usb_tx_append_d(&p, pe, (double)g_fault.shaft_hz_refined, 2u);
  usb_tx_append_sn(&p, pe, "%s", " LS=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_ls, 3u);
  usb_tx_append_sn(&p, pe, "%s", " FFT=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_fft, 3u);
  usb_tx_append_sn(&p, pe, "%s", " CF=");
  usb_tx_append_d(&p, pe, (double)g_fault.detect_confidence, 2u);
  usb_tx_append_sn(&p, pe, "%s", " MI=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_music, 3u);
  usb_tx_append_sn(&p, pe, "%s", " ES=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_esprit, 3u);
  usb_tx_append_sn(&p, pe, "%s", " TK=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_teager, 3u);
  usb_tx_append_sn(&p, pe, "%s", " SK=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_sk, 3u);
  usb_tx_append_sn(&p, pe, "%s", " WV=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_wavelet, 3u);
  usb_tx_append_sn(&p, pe, "%s", " CY=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_cyclic, 3u);
  usb_tx_append_sn(&p, pe, "%s", " SB=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_sideband, 3u);
  usb_tx_append_sn(&p, pe, "%s", " ACF=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_env_acf, 3u);
  usb_tx_append_sn(&p, pe, "%s", " WL=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_welch, 3u);
  usb_tx_append_sn(&p, pe, "%s", " COH=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_coherence, 3u);
  usb_tx_append_sn(&p, pe, "%s", " SKPK=");
  usb_tx_append_d(&p, pe, (double)sk_max, 3u);
  usb_tx_append_sn(&p, pe, "%s", "@");
  usb_tx_append_d(&p, pe, (double)sk_f_hz, 1u);
  usb_tx_append_sn(&p, pe, "%s", "Hz");
  usb_tx_append_sn(&p, pe, " KB=%u LV=%u\r\n",
                   (unsigned)g_fault.kurt_band_fc_hz, (unsigned)g_fault.fault_level);
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen((char *)usb_cdc_tx_buf));

  /* ---- Line 1b: Per-fault scores + EMA/CUSUM ---- */
  p = (char *)usb_cdc_tx_buf;
  usb_tx_append_sn(&p, pe, "%s", "PF O=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_bpfo, 3u);
  usb_tx_append_sn(&p, pe, "%s", " I=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_bpfi, 3u);
  usb_tx_append_sn(&p, pe, "%s", " B=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_bsf, 3u);
  usb_tx_append_sn(&p, pe, "%s", " T=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_ftf, 3u);
  usb_tx_append_sn(&p, pe, " DOM=%u EMA=", (unsigned)g_fault.dominant_fault);
  usb_tx_append_d(&p, pe, (double)g_fault.fault_index_ema, 3u);
  usb_tx_append_sn(&p, pe, "%s", " CU=");
  usb_tx_append_d(&p, pe, (double)g_fault.cusum_score, 3u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen((char *)usb_cdc_tx_buf));

  p = (char *)usb_cdc_tx_buf;
  usb_tx_append_sn(&p, pe, "%s", "NP F=");
  usb_tx_append_d(&p, pe, (double)g_fault.index_ls, 3u);
  usb_tx_append_sn(&p, pe, "/"); usb_tx_append_d(&p, pe, (double)g_fault.np_gamma_fourier, 3u);
  usb_tx_append_sn(&p, pe, " aF="); usb_tx_append_d(&p, pe, (double)g_fault.amp_fourier_fault, 4u);
  usb_tx_append_sn(&p, pe, " M="); usb_tx_append_d(&p, pe, (double)g_fault.index_music, 3u);
  usb_tx_append_sn(&p, pe, "/"); usb_tx_append_d(&p, pe, (double)g_fault.np_gamma_music, 3u);
  usb_tx_append_sn(&p, pe, " E="); usb_tx_append_d(&p, pe, (double)g_fault.index_esprit, 3u);
  usb_tx_append_sn(&p, pe, "/"); usb_tx_append_d(&p, pe, (double)g_fault.np_gamma_esprit, 3u);
  usb_tx_append_sn(&p, pe, " AL=");
  usb_tx_append_d(&p, pe, (double)g_fault.np_alarm_fourier, 0u);
  usb_tx_append_sn(&p, pe, "/");
  usb_tx_append_d(&p, pe, (double)g_fault.np_alarm_music, 0u);
  usb_tx_append_sn(&p, pe, "/");
  usb_tx_append_d(&p, pe, (double)g_fault.np_alarm_esprit, 0u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen((char *)usb_cdc_tx_buf));

#if USB_FULL_SPECTRAL_EXPORT
  USB_Send_SpectralFigures();
#endif

  /* ---- Line 2: Stator winding summary ---- */
  p = (char *)usb_cdc_tx_buf;
  usb_tx_append_sn(&p, pe, "%s", "ST f=");
  usb_tx_append_d(&p, pe, (double)g_supply_line_hz, 1u);
  usb_tx_append_sn(&p, pe, " SH=%u GD=%u ES=%u EG=%u",
                   (unsigned)g_fault.stator_fault_short,
                   (unsigned)g_fault.stator_fault_gnd,
                   (unsigned)g_fault.stator_early_short,
                   (unsigned)g_fault.stator_early_gnd);
  usb_tx_append_sn(&p, pe, " NSR=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_nsr, 4u);
  usb_tx_append_sn(&p, pe, "%s", " ZSR=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_zsr, 4u);
  usb_tx_append_sn(&p, pe, "%s", " NT=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_nsr_td, 4u);
  usb_tx_append_sn(&p, pe, "%s", " ZT=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_zsr_td, 4u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen((char *)usb_cdc_tx_buf));

  p = (char *)usb_cdc_tx_buf;
  usb_tx_append_sn(&p, pe, "%s", "ST2 SI=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_short_index_ema, 3u);
  usb_tx_append_sn(&p, pe, "%s", " GI=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_gnd_index_ema, 3u);
  usb_tx_append_sn(&p, pe, "%s", " CS=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_cusum_short, 3u);
  usb_tx_append_sn(&p, pe, "%s", " CG=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_cusum_gnd, 3u);
  usb_tx_append_sn(&p, pe, "%s", " H=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_harm_ratio, 4u);
  usb_tx_append_sn(&p, pe, "%s", " O=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_odd_harm_index, 4u);
  usb_tx_append_sn(&p, pe, "%s", " PS=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_phase_spread_deg, 2u);
  usb_tx_append_sn(&p, pe, "%s", " IMB=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_imbalance_pct, 2u);
  usb_tx_append_sn(&p, pe, " LV=%u\r\n", (unsigned)g_fault.stator_fault_level);
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen((char *)usb_cdc_tx_buf));

  /* ---- Line 3: Sequence component magnitudes ---- */
  p = (char *)usb_cdc_tx_buf;
  usb_tx_append_sn(&p, pe, "%s", "ST2 I1=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_i1_mag, 6u);
  usb_tx_append_sn(&p, pe, "%s", " I0=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_i0_mag, 6u);
  usb_tx_append_sn(&p, pe, "%s", " I2=");
  usb_tx_append_d(&p, pe, (double)g_fault.stator_i2_mag, 6u);
  usb_tx_append_sn(&p, pe, "%s", "\r\n");
  *p = '\0';
  USB_Tx_WaitSend((uint16_t)strlen((char *)usb_cdc_tx_buf));
}

/* USER CODE END 0 */

/**
  * @brief  The application entry point.
  * @retval int
  */
int main(void)
{
  /* USER CODE BEGIN 1 */
  /* USER CODE END 1 */

  /* MPU Configuration */
  MPU_Config();

#if defined(APP_EXECUTE_FROM_QSPI)
  /* Safe for XIP from QSPI after WeAct bootloader jump (cache was off before jump). */
  SCB_EnableICache();
  SCB_EnableDCache();
#endif

  /* MCU Configuration */
  HAL_Init();

  /* USER CODE BEGIN Init */
  /* USER CODE END Init */

  /* Configure the system clock */
  SystemClock_Config();

  /* Configure the peripherals common clocks */
  PeriphCommonClock_Config();

  /* USER CODE BEGIN SysInit */
  /* USER CODE END SysInit */

  /* Initialize all configured peripherals */
  MX_GPIO_Init();
  MX_DMA_Init();
  MX_QUADSPI_Init();
  MX_USB_DEVICE_Init();
  MX_ADC1_Init();
  MX_ADC2_Init();
  MX_ADC3_Init();
  MX_SPI1_Init();
  MX_SPI4_Init();
  MX_TIM2_Init();
  MX_TIM1_Init();

  /* USER CODE BEGIN 2 */

  App_Clock_Init_Default();

  SPI1Flash_Init();

  /* Brief pause helps host USB enumeration (WeAct + Windows) */
  HAL_Delay(150);

  /* QSPI: memory-mapped mode for splash/assets @ 0x90000000 (program external flash) */
  g_qspi_ok = QSPI_WaitReady(100u);
  if (g_qspi_ok) {
    QSPI_Assets_Init();
  }

  /* Initialize TFT display (ST7735 0.96" 160x80 on SPI4) */
  TFT_Init();





  /* Initialize detection helpers */
  Window_Generate_Hamming();
  Reset_SampleFlags();
  g_fault.fault_index = 0.0f;
  g_fault.fault_index_ema = 0.0f;
  g_fault.cusum_score = 0.0f;
  g_fault.fault_level = 0u;

  /* QSPI parameter persistence (indirect R/W; leaves mmap around each op) */
  if (!g_qspi_ok) {
    g_qspi_ok = QSPI_WaitReady(100u);
    if (g_qspi_ok) {
      QSPI_Assets_Init();
    }
  }
  if (!Params_Load_From_QSPI())
  {
    Params_Load_Defaults();
  }
  (void)Baseline_Load_From_QSPI();
  (void)StatorBaseline_Load_From_QSPI();

  /* ADC offset calibration — recovers 1-2 LSBs of 12-bit accuracy */
  HAL_ADCEx_Calibration_Start(&hadc1, ADC_CALIB_OFFSET, ADC_DIFFERENTIAL_ENDED);
  HAL_ADCEx_Calibration_Start(&hadc2, ADC_CALIB_OFFSET, ADC_DIFFERENTIAL_ENDED);
  HAL_ADCEx_Calibration_Start(&hadc3, ADC_CALIB_OFFSET, ADC_DIFFERENTIAL_ENDED);

  /* Start synchronized ADC sampling (DMA) */
  HAL_ADC_Start_DMA(&hadc1, (uint32_t *)raw_adc1, FP_SAMPLES_PER_PHASE);
  HAL_ADC_Start_DMA(&hadc2, (uint32_t *)raw_adc2, FP_SAMPLES_PER_PHASE);
  HAL_ADC_Start_DMA(&hadc3, (uint32_t *)raw_adc3, FP_SAMPLES_PER_PHASE);

  /* TIM2 TRGO triggers all 3 ADCs */
  HAL_TIM_Base_Start(&htim2);

  /* Clear boot screen, show initial empty layout */
  HAL_Delay(500);
  TFT_Display_Update(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);

  /* USER CODE END 2 */

  /* Infinite loop */
  /* USER CODE BEGIN WHILE */
  while (1)
  {
    /* USER CODE END WHILE */

    /* USER CODE BEGIN 3 */

    App_Clock_Service();

    static uint32_t wait_start_ms = 0u;
    if (wait_start_ms == 0u) wait_start_ms = HAL_GetTick();

    /* ---- Process USB commands ---- */
    if (g_usb_rx_ready)
    {
      g_usb_rx_ready = 0u;
      USB_Process_Received_Data();
    }

    /* ---- Process ADC data when all 3 channels complete ---- */
    if (g_all_ready)
    {
      /* DSB: ensure DMA writes are fully visible to CPU */
      __DSB();

      /* Stop triggers during processing */
      HAL_TIM_Base_Stop(&htim2);
      HAL_ADC_Stop_DMA(&hadc1);
      HAL_ADC_Stop_DMA(&hadc2);
      HAL_ADC_Stop_DMA(&hadc3);

      /* Invalidate D-Cache for DMA-written buffers */
      SCB_InvalidateDCache_by_Addr((uint32_t *)raw_adc1, sizeof(raw_adc1));
      SCB_InvalidateDCache_by_Addr((uint32_t *)raw_adc2, sizeof(raw_adc2));
      SCB_InvalidateDCache_by_Addr((uint32_t *)raw_adc3, sizeof(raw_adc3));

      /* Service any pending USB command (PING etc.) during DSP */
      USB_ServiceIfPending();

      /* Save ADC snapshot if requested */
      if (g_adc_save_requested)
      {
        (void)ADC_SaveSnapshot_To_SPI1Flash();
        g_adc_save_requested = 0u;
      }

      /* Per-channel ADC offset from idle capture (before current conversion) */
      ADC_TryAutoCalibrateOffsets();

      /* Convert raw ADC to current (Amperes) */
      ADC_Raw_To_Current();

      /* Remove DC offset */
      Remove_DC(phase_a, FP_SAMPLES_PER_PHASE);
      Remove_DC(phase_b, FP_SAMPLES_PER_PHASE);
      Remove_DC(phase_c, FP_SAMPLES_PER_PHASE);

      /* Service any pending USB command (PING etc.) during DSP */
      USB_ServiceIfPending();

      /* Keep unwindowed currents for PHASECSV export on PC side. */
      memcpy(g_phase_a_csv, phase_a, sizeof(g_phase_a_csv));
      memcpy(g_phase_b_csv, phase_b, sizeof(g_phase_b_csv));
      memcpy(g_phase_c_csv, phase_c, sizeof(g_phase_c_csv));
      g_phase_csv_ready = 1u;

      /* RMS on true (un-windowed) current */
      const float ia_rms = Phase_Rms_A(phase_a, FP_SAMPLES_PER_PHASE);
      const float ib_rms = Phase_Rms_A(phase_b, FP_SAMPLES_PER_PHASE);
      const float ic_rms = Phase_Rms_A(phase_c, FP_SAMPLES_PER_PHASE);

      /* ============================================================
       * STATOR WINDING ANALYSIS — on UN-WINDOWED data
       * Fortescue phasor magnitudes (I0, I1, I2) must be measured on
       * the raw current, not attenuated by a Hamming window (~54% bias).
       * ============================================================
       */
      Stator_Winding_Analyze(g_supply_line_hz);

      /* Service any pending USB command (PING etc.) during DSP */
      USB_ServiceIfPending();

      /* ============================================================
       * BEARING FAULT DETECTION — on UN-WINDOWED (DC-removed) current
       * Parametric/envelope methods (LS sinusoid fit, MUSIC, ESPRIT,
       * Teager-Kaiser, kurtogram, wavelet, squared-envelope ACF, 2nd-order
       * cyclostationary) are unbiased on the raw signal; a Hamming taper
       * would bias LS amplitudes, taper bearing impacts at the frame edges,
       * and double-window the self-windowing Welch PSD/coherence path.
       * The only leakage-sensitive method, the plain |FFT| magnitude, applies
       * its own Hamming window internally (FFT_ComputeOneSidedMag_Half).
       * ============================================================
       */
      Fault_Detect_Bearing();

      /* Service any pending USB command (PING etc.) during DSP */
      USB_ServiceIfPending();

      /* ============================================================
       * UPDATE DISPLAY — color-coded fault/warning indicators
       * ============================================================
       */

      TFT_Display_Update(ia_rms, ib_rms, ic_rms,
                         g_fault.bpfo_hz, g_fault.bpfi_hz,
                         g_fault.ftf_hz, g_fault.bsf_hz);

      /* Send results via USB CDC — binary or text depending on STREAM= mode */
      if (g_usb_binary_stream)
        USB_Send_FaultBinaryFrame();
      else
        USB_Send_FaultSummary();

      /* Service any pending USB command (PING etc.) during DSP */
      USB_ServiceIfPending();

      /* Restart acquisition cycle */
      Reset_SampleFlags();
      wait_start_ms = HAL_GetTick();

      HAL_ADC_Start_DMA(&hadc1, (uint32_t *)raw_adc1, FP_SAMPLES_PER_PHASE);
      HAL_ADC_Start_DMA(&hadc2, (uint32_t *)raw_adc2, FP_SAMPLES_PER_PHASE);
      HAL_ADC_Start_DMA(&hadc3, (uint32_t *)raw_adc3, FP_SAMPLES_PER_PHASE);
      HAL_TIM_Base_Start(&htim2);
    }
    else
    {
      /* Watchdog: re-arm if ADC sync fails after 2 seconds */
      if ((HAL_GetTick() - wait_start_ms) > 2000u)
      {
        HAL_TIM_Base_Stop(&htim2);
        HAL_ADC_Stop_DMA(&hadc1);
        HAL_ADC_Stop_DMA(&hadc2);
        HAL_ADC_Stop_DMA(&hadc3);

        Reset_SampleFlags();
        wait_start_ms = HAL_GetTick();

        HAL_ADC_Start_DMA(&hadc1, (uint32_t *)raw_adc1, FP_SAMPLES_PER_PHASE);
        HAL_ADC_Start_DMA(&hadc2, (uint32_t *)raw_adc2, FP_SAMPLES_PER_PHASE);
        HAL_ADC_Start_DMA(&hadc3, (uint32_t *)raw_adc3, FP_SAMPLES_PER_PHASE);
        HAL_TIM_Base_Start(&htim2);
      }
      HAL_Delay(1);
    }

  }
  /* USER CODE END 3 */
}

/* ============================================================================
 * All peripheral init functions below — UNCHANGED from CubeMX
 * ============================================================================
 */

void SystemClock_Config(void)
{
  RCC_OscInitTypeDef RCC_OscInitStruct = {0};
  RCC_ClkInitTypeDef RCC_ClkInitStruct = {0};
  HAL_PWREx_ConfigSupply(PWR_LDO_SUPPLY);
  __HAL_PWR_VOLTAGESCALING_CONFIG(PWR_REGULATOR_VOLTAGE_SCALE2);
  while(!__HAL_PWR_GET_FLAG(PWR_FLAG_VOSRDY)) {}
  RCC_OscInitStruct.OscillatorType = RCC_OSCILLATORTYPE_HSE;
  RCC_OscInitStruct.HSEState = RCC_HSE_ON;
  RCC_OscInitStruct.PLL.PLLState = RCC_PLL_ON;
  RCC_OscInitStruct.PLL.PLLSource = RCC_PLLSOURCE_HSE;
  RCC_OscInitStruct.PLL.PLLM = 5;
  RCC_OscInitStruct.PLL.PLLN = 96;
  RCC_OscInitStruct.PLL.PLLP = 2;
  RCC_OscInitStruct.PLL.PLLQ = 4;
  RCC_OscInitStruct.PLL.PLLR = 2;
  RCC_OscInitStruct.PLL.PLLRGE = RCC_PLL1VCIRANGE_2;
  RCC_OscInitStruct.PLL.PLLVCOSEL = RCC_PLL1VCOWIDE;
  RCC_OscInitStruct.PLL.PLLFRACN = 0;
  if (HAL_RCC_OscConfig(&RCC_OscInitStruct) != HAL_OK) Error_Handler();
  RCC_ClkInitStruct.ClockType = RCC_CLOCKTYPE_HCLK|RCC_CLOCKTYPE_SYSCLK
                                |RCC_CLOCKTYPE_PCLK1|RCC_CLOCKTYPE_PCLK2
                                |RCC_CLOCKTYPE_D3PCLK1|RCC_CLOCKTYPE_D1PCLK1;
  RCC_ClkInitStruct.SYSCLKSource = RCC_SYSCLKSOURCE_PLLCLK;
  RCC_ClkInitStruct.SYSCLKDivider = RCC_SYSCLK_DIV1;
  RCC_ClkInitStruct.AHBCLKDivider = RCC_HCLK_DIV2;
  RCC_ClkInitStruct.APB3CLKDivider = RCC_APB3_DIV2;
  RCC_ClkInitStruct.APB1CLKDivider = RCC_APB1_DIV2;
  RCC_ClkInitStruct.APB2CLKDivider = RCC_APB2_DIV2;
  RCC_ClkInitStruct.APB4CLKDivider = RCC_APB4_DIV2;
  if (HAL_RCC_ClockConfig(&RCC_ClkInitStruct, FLASH_LATENCY_2) != HAL_OK) Error_Handler();
}

void PeriphCommonClock_Config(void)
{
  RCC_PeriphCLKInitTypeDef PeriphClkInitStruct = {0};
  PeriphClkInitStruct.PeriphClockSelection = RCC_PERIPHCLK_USB|RCC_PERIPHCLK_ADC|RCC_PERIPHCLK_SPI4;
  PeriphClkInitStruct.PLL2.PLL2M = 2;
  PeriphClkInitStruct.PLL2.PLL2N = 12;
  PeriphClkInitStruct.PLL2.PLL2P = 2;
  PeriphClkInitStruct.PLL2.PLL2Q = 3;
  PeriphClkInitStruct.PLL2.PLL2R = 1;
  PeriphClkInitStruct.PLL2.PLL2RGE = RCC_PLL2VCIRANGE_3;
  PeriphClkInitStruct.PLL2.PLL2VCOSEL = RCC_PLL2VCOMEDIUM;
  PeriphClkInitStruct.PLL2.PLL2FRACN = 0;
  PeriphClkInitStruct.PLL3.PLL3M = 10;
  PeriphClkInitStruct.PLL3.PLL3N = 96;
  PeriphClkInitStruct.PLL3.PLL3P = 5;
  PeriphClkInitStruct.PLL3.PLL3Q = 5;
  PeriphClkInitStruct.PLL3.PLL3R = 5;
  PeriphClkInitStruct.PLL3.PLL3RGE = RCC_PLL3VCIRANGE_1;
  PeriphClkInitStruct.PLL3.PLL3VCOSEL = RCC_PLL3VCOWIDE;
  PeriphClkInitStruct.PLL3.PLL3FRACN = 0;
  PeriphClkInitStruct.Spi45ClockSelection = RCC_SPI45CLKSOURCE_PLL3;
  PeriphClkInitStruct.UsbClockSelection = RCC_USBCLKSOURCE_PLL3;
  PeriphClkInitStruct.AdcClockSelection = RCC_ADCCLKSOURCE_PLL2;
  if (HAL_RCCEx_PeriphCLKConfig(&PeriphClkInitStruct) != HAL_OK) Error_Handler();
}

static void MX_ADC1_Init(void)
{
  ADC_MultiModeTypeDef multimode = {0};
  ADC_ChannelConfTypeDef sConfig = {0};
  hadc1.Instance = ADC1;
  hadc1.Init.ScanConvMode = ADC_SCAN_DISABLE;
  hadc1.Init.EOCSelection = ADC_EOC_SINGLE_CONV;
  hadc1.Init.LowPowerAutoWait = DISABLE;
  hadc1.Init.ContinuousConvMode = DISABLE;
  hadc1.Init.NbrOfConversion = 1;
  hadc1.Init.DiscontinuousConvMode = DISABLE;
  hadc1.Init.ExternalTrigConv = ADC_EXTERNALTRIG_T2_TRGO;
  hadc1.Init.ExternalTrigConvEdge = ADC_EXTERNALTRIGCONVEDGE_RISING;
  hadc1.Init.ConversionDataManagement = ADC_CONVERSIONDATA_DR;
  hadc1.Init.Overrun = ADC_OVR_DATA_PRESERVED;
  hadc1.Init.LeftBitShift = ADC_LEFTBITSHIFT_NONE;
  hadc1.Init.OversamplingMode = DISABLE;
  hadc1.Init.Oversampling.Ratio = 1;
  if (HAL_ADC_Init(&hadc1) != HAL_OK) Error_Handler();
  hadc1.Init.ClockPrescaler = ADC_CLOCK_ASYNC_DIV2;
  hadc1.Init.Resolution = ADC_RESOLUTION_12B;
  /* Hardware 16x oversample + /16 shift: lower quantization noise (longer per-sample conversion). */
  hadc1.Init.OversamplingMode = ENABLE;
  hadc1.Init.Oversampling.Ratio = 15u; /* RM: ratio = OSR+1 → 16 conversions per trigger */
  hadc1.Init.Oversampling.RightBitShift = ADC_RIGHTBITSHIFT_4;
  hadc1.Init.Oversampling.TriggeredMode = ADC_TRIGGEREDMODE_SINGLE_TRIGGER;
  hadc1.Init.Oversampling.OversamplingStopReset = ADC_REGOVERSAMPLING_CONTINUED_MODE;
  if (HAL_ADC_Init(&hadc1) != HAL_OK) Error_Handler();
  multimode.Mode = ADC_MODE_INDEPENDENT;
  if (HAL_ADCEx_MultiModeConfigChannel(&hadc1, &multimode) != HAL_OK) Error_Handler();
  sConfig.Channel = ADC_CHANNEL_5;
  sConfig.Rank = ADC_REGULAR_RANK_1;
  sConfig.SamplingTime = ADC_SAMPLETIME_64CYCLES_5;
  sConfig.SingleDiff = ADC_DIFFERENTIAL_ENDED;
  sConfig.OffsetNumber = ADC_OFFSET_NONE;
  sConfig.Offset = 0;
  sConfig.OffsetSignedSaturation = DISABLE;
  if (HAL_ADC_ConfigChannel(&hadc1, &sConfig) != HAL_OK) Error_Handler();
}

static void MX_ADC2_Init(void)
{
  ADC_ChannelConfTypeDef sConfig = {0};
  hadc2.Instance = ADC2;
  hadc2.Init.ScanConvMode = ADC_SCAN_DISABLE;
  hadc2.Init.EOCSelection = ADC_EOC_SINGLE_CONV;
  hadc2.Init.LowPowerAutoWait = DISABLE;
  hadc2.Init.ContinuousConvMode = DISABLE;
  hadc2.Init.NbrOfConversion = 1;
  hadc2.Init.DiscontinuousConvMode = DISABLE;
  hadc2.Init.ExternalTrigConv = ADC_EXTERNALTRIG_T2_TRGO;
  hadc2.Init.ExternalTrigConvEdge = ADC_EXTERNALTRIGCONVEDGE_RISING;
  hadc2.Init.ConversionDataManagement = ADC_CONVERSIONDATA_DR;
  hadc2.Init.Overrun = ADC_OVR_DATA_PRESERVED;
  hadc2.Init.LeftBitShift = ADC_LEFTBITSHIFT_NONE;
  hadc2.Init.OversamplingMode = DISABLE;
  hadc2.Init.Oversampling.Ratio = 1;
  if (HAL_ADC_Init(&hadc2) != HAL_OK) Error_Handler();
  hadc2.Init.ClockPrescaler = ADC_CLOCK_ASYNC_DIV2;
  hadc2.Init.Resolution = ADC_RESOLUTION_12B;
  hadc2.Init.OversamplingMode = ENABLE;
  hadc2.Init.Oversampling.Ratio = 15u;
  hadc2.Init.Oversampling.RightBitShift = ADC_RIGHTBITSHIFT_4;
  hadc2.Init.Oversampling.TriggeredMode = ADC_TRIGGEREDMODE_SINGLE_TRIGGER;
  hadc2.Init.Oversampling.OversamplingStopReset = ADC_REGOVERSAMPLING_CONTINUED_MODE;
  if (HAL_ADC_Init(&hadc2) != HAL_OK) Error_Handler();
  sConfig.Channel = ADC_CHANNEL_4;
  sConfig.Rank = ADC_REGULAR_RANK_1;
  sConfig.SamplingTime = ADC_SAMPLETIME_64CYCLES_5;
  sConfig.SingleDiff = ADC_DIFFERENTIAL_ENDED;
  sConfig.OffsetNumber = ADC_OFFSET_NONE;
  sConfig.Offset = 0;
  sConfig.OffsetSignedSaturation = DISABLE;
  if (HAL_ADC_ConfigChannel(&hadc2, &sConfig) != HAL_OK) Error_Handler();
}

static void MX_ADC3_Init(void)
{
  ADC_ChannelConfTypeDef sConfig = {0};
  hadc3.Instance = ADC3;
  hadc3.Init.ClockPrescaler = ADC_CLOCK_ASYNC_DIV1;
  hadc3.Init.ScanConvMode = ADC_SCAN_DISABLE;
  hadc3.Init.EOCSelection = ADC_EOC_SINGLE_CONV;
  hadc3.Init.LowPowerAutoWait = DISABLE;
  hadc3.Init.ContinuousConvMode = DISABLE;
  hadc3.Init.NbrOfConversion = 1;
  hadc3.Init.DiscontinuousConvMode = DISABLE;
  hadc3.Init.ExternalTrigConv = ADC_EXTERNALTRIG_T2_TRGO;
  hadc3.Init.ExternalTrigConvEdge = ADC_EXTERNALTRIGCONVEDGE_RISING;
  hadc3.Init.ConversionDataManagement = ADC_CONVERSIONDATA_DR;
  hadc3.Init.Overrun = ADC_OVR_DATA_PRESERVED;
  hadc3.Init.LeftBitShift = ADC_LEFTBITSHIFT_NONE;
  hadc3.Init.OversamplingMode = DISABLE;
  hadc3.Init.Oversampling.Ratio = 1;
  if (HAL_ADC_Init(&hadc3) != HAL_OK) Error_Handler();
  hadc3.Init.ClockPrescaler = ADC_CLOCK_ASYNC_DIV2;
  hadc3.Init.Resolution = ADC_RESOLUTION_12B;
  hadc3.Init.OversamplingMode = ENABLE;
  hadc3.Init.Oversampling.Ratio = 15u;
  hadc3.Init.Oversampling.RightBitShift = ADC_RIGHTBITSHIFT_4;
  hadc3.Init.Oversampling.TriggeredMode = ADC_TRIGGEREDMODE_SINGLE_TRIGGER;
  hadc3.Init.Oversampling.OversamplingStopReset = ADC_REGOVERSAMPLING_CONTINUED_MODE;
  if (HAL_ADC_Init(&hadc3) != HAL_OK) Error_Handler();
  sConfig.Channel = ADC_CHANNEL_1;
  sConfig.Rank = ADC_REGULAR_RANK_1;
  sConfig.SamplingTime = ADC_SAMPLETIME_64CYCLES_5;
  sConfig.SingleDiff = ADC_DIFFERENTIAL_ENDED;
  sConfig.OffsetNumber = ADC_OFFSET_NONE;
  sConfig.Offset = 0;
  sConfig.OffsetSignedSaturation = DISABLE;
  if (HAL_ADC_ConfigChannel(&hadc3, &sConfig) != HAL_OK) Error_Handler();
}

static void MX_QUADSPI_Init(void)
{
  hqspi.Instance = QUADSPI;
  hqspi.Init.ClockPrescaler = 2;
  hqspi.Init.FifoThreshold = 1;
  hqspi.Init.SampleShifting = QSPI_SAMPLE_SHIFTING_HALFCYCLE;
  hqspi.Init.FlashSize = 22;
  hqspi.Init.ChipSelectHighTime = QSPI_CS_HIGH_TIME_1_CYCLE;
  hqspi.Init.ClockMode = QSPI_CLOCK_MODE_0;
  hqspi.Init.FlashID = QSPI_FLASH_ID_1;
  hqspi.Init.DualFlash = QSPI_DUALFLASH_DISABLE;
  if (HAL_QSPI_Init(&hqspi) != HAL_OK) Error_Handler();
}

static void MX_SPI1_Init(void)
{
  hspi1.Instance = SPI1;
  hspi1.Init.Mode = SPI_MODE_MASTER;
  hspi1.Init.Direction = SPI_DIRECTION_2LINES;
  hspi1.Init.DataSize = SPI_DATASIZE_8BIT;
  hspi1.Init.CLKPolarity = SPI_POLARITY_LOW;
  hspi1.Init.CLKPhase = SPI_PHASE_1EDGE;
  hspi1.Init.NSS = SPI_NSS_SOFT;
  hspi1.Init.BaudRatePrescaler = SPI_BAUDRATEPRESCALER_2;
  hspi1.Init.FirstBit = SPI_FIRSTBIT_MSB;
  hspi1.Init.TIMode = SPI_TIMODE_DISABLE;
  hspi1.Init.CRCCalculation = SPI_CRCCALCULATION_DISABLE;
  hspi1.Init.CRCPolynomial = 0x0;
  hspi1.Init.NSSPMode = SPI_NSS_PULSE_ENABLE;
  hspi1.Init.NSSPolarity = SPI_NSS_POLARITY_LOW;
  hspi1.Init.FifoThreshold = SPI_FIFO_THRESHOLD_01DATA;
  hspi1.Init.TxCRCInitializationPattern = SPI_CRC_INITIALIZATION_ALL_ZERO_PATTERN;
  hspi1.Init.RxCRCInitializationPattern = SPI_CRC_INITIALIZATION_ALL_ZERO_PATTERN;
  hspi1.Init.MasterSSIdleness = SPI_MASTER_SS_IDLENESS_00CYCLE;
  hspi1.Init.MasterInterDataIdleness = SPI_MASTER_INTERDATA_IDLENESS_00CYCLE;
  hspi1.Init.MasterReceiverAutoSusp = SPI_MASTER_RX_AUTOSUSP_DISABLE;
  hspi1.Init.MasterKeepIOState = SPI_MASTER_KEEP_IO_STATE_DISABLE;
  hspi1.Init.IOSwap = SPI_IO_SWAP_DISABLE;
  if (HAL_SPI_Init(&hspi1) != HAL_OK) Error_Handler();
}

static void MX_SPI4_Init(void)
{
  hspi4.Instance = SPI4;
  hspi4.Init.Mode = SPI_MODE_MASTER;
  hspi4.Init.Direction = SPI_DIRECTION_1LINE;
  hspi4.Init.DataSize = SPI_DATASIZE_8BIT;
  hspi4.Init.CLKPolarity = SPI_POLARITY_LOW;
  hspi4.Init.CLKPhase = SPI_PHASE_1EDGE;
  hspi4.Init.NSS = SPI_NSS_SOFT;
  hspi4.Init.BaudRatePrescaler = SPI_BAUDRATEPRESCALER_8;
  hspi4.Init.FirstBit = SPI_FIRSTBIT_MSB;
  hspi4.Init.TIMode = SPI_TIMODE_DISABLE;
  hspi4.Init.CRCCalculation = SPI_CRCCALCULATION_DISABLE;
  hspi4.Init.CRCPolynomial = 0x0;
  hspi4.Init.NSSPMode = SPI_NSS_PULSE_ENABLE;
  hspi4.Init.NSSPolarity = SPI_NSS_POLARITY_LOW;
  hspi4.Init.FifoThreshold = SPI_FIFO_THRESHOLD_01DATA;
  hspi4.Init.TxCRCInitializationPattern = SPI_CRC_INITIALIZATION_ALL_ZERO_PATTERN;
  hspi4.Init.RxCRCInitializationPattern = SPI_CRC_INITIALIZATION_ALL_ZERO_PATTERN;
  hspi4.Init.MasterSSIdleness = SPI_MASTER_SS_IDLENESS_00CYCLE;
  hspi4.Init.MasterInterDataIdleness = SPI_MASTER_INTERDATA_IDLENESS_00CYCLE;
  hspi4.Init.MasterReceiverAutoSusp = SPI_MASTER_RX_AUTOSUSP_DISABLE;
  hspi4.Init.MasterKeepIOState = SPI_MASTER_KEEP_IO_STATE_DISABLE;
  hspi4.Init.IOSwap = SPI_IO_SWAP_DISABLE;
  if (HAL_SPI_Init(&hspi4) != HAL_OK) Error_Handler();
}

static void MX_TIM1_Init(void)
{
  TIM_OC_InitTypeDef sConfigOC = {0};
  TIM_BreakDeadTimeConfigTypeDef sBreakDeadTimeConfig = {0};
  htim1.Instance = TIM1;
  htim1.Init.Prescaler = 2399;
  htim1.Init.CounterMode = TIM_COUNTERMODE_UP;
  htim1.Init.Period = 100;
  htim1.Init.ClockDivision = TIM_CLOCKDIVISION_DIV1;
  htim1.Init.RepetitionCounter = 0;
  htim1.Init.AutoReloadPreload = TIM_AUTORELOAD_PRELOAD_DISABLE;
  if (HAL_TIM_PWM_Init(&htim1) != HAL_OK) Error_Handler();
  sConfigOC.OCMode = TIM_OCMODE_PWM1;
  sConfigOC.Pulse = 0;
  sConfigOC.OCPolarity = TIM_OCPOLARITY_HIGH;
  sConfigOC.OCNPolarity = TIM_OCNPOLARITY_LOW;
  sConfigOC.OCFastMode = TIM_OCFAST_DISABLE;
  sConfigOC.OCIdleState = TIM_OCIDLESTATE_RESET;
  sConfigOC.OCNIdleState = TIM_OCNIDLESTATE_RESET;
  if (HAL_TIM_PWM_ConfigChannel(&htim1, &sConfigOC, TIM_CHANNEL_2) != HAL_OK) Error_Handler();
  sBreakDeadTimeConfig.OffStateRunMode = TIM_OSSR_DISABLE;
  sBreakDeadTimeConfig.OffStateIDLEMode = TIM_OSSI_DISABLE;
  sBreakDeadTimeConfig.LockLevel = TIM_LOCKLEVEL_OFF;
  sBreakDeadTimeConfig.DeadTime = 0;
  sBreakDeadTimeConfig.BreakState = TIM_BREAK_DISABLE;
  sBreakDeadTimeConfig.BreakPolarity = TIM_BREAKPOLARITY_HIGH;
  sBreakDeadTimeConfig.BreakFilter = 0;
  sBreakDeadTimeConfig.Break2State = TIM_BREAK2_DISABLE;
  sBreakDeadTimeConfig.Break2Polarity = TIM_BREAK2POLARITY_HIGH;
  sBreakDeadTimeConfig.Break2Filter = 0;
  sBreakDeadTimeConfig.AutomaticOutput = TIM_AUTOMATICOUTPUT_DISABLE;
  if (HAL_TIMEx_ConfigBreakDeadTime(&htim1, &sBreakDeadTimeConfig) != HAL_OK) Error_Handler();
}

static void MX_TIM2_Init(void)
{
  TIM_ClockConfigTypeDef sClockSourceConfig = {0};
  TIM_MasterConfigTypeDef sMasterConfig = {0};
  htim2.Instance = TIM2;
  htim2.Init.Prescaler = 2399;
  htim2.Init.CounterMode = TIM_COUNTERMODE_UP;
  htim2.Init.Period = 9;
  htim2.Init.ClockDivision = TIM_CLOCKDIVISION_DIV1;
  htim2.Init.AutoReloadPreload = TIM_AUTORELOAD_PRELOAD_DISABLE;
  if (HAL_TIM_Base_Init(&htim2) != HAL_OK) Error_Handler();
  sClockSourceConfig.ClockSource = TIM_CLOCKSOURCE_INTERNAL;
  if (HAL_TIM_ConfigClockSource(&htim2, &sClockSourceConfig) != HAL_OK) Error_Handler();
  sMasterConfig.MasterOutputTrigger = TIM_TRGO_UPDATE;
  sMasterConfig.MasterSlaveMode = TIM_MASTERSLAVEMODE_DISABLE;
  if (HAL_TIMEx_MasterConfigSynchronization(&htim2, &sMasterConfig) != HAL_OK) Error_Handler();
}

static void MX_DMA_Init(void)
{
  __HAL_RCC_DMA1_CLK_ENABLE();
  HAL_NVIC_SetPriority(DMA1_Stream0_IRQn, 0, 0); HAL_NVIC_EnableIRQ(DMA1_Stream0_IRQn);
  HAL_NVIC_SetPriority(DMA1_Stream1_IRQn, 0, 0); HAL_NVIC_EnableIRQ(DMA1_Stream1_IRQn);
  HAL_NVIC_SetPriority(DMA1_Stream2_IRQn, 0, 0); HAL_NVIC_EnableIRQ(DMA1_Stream2_IRQn);
  HAL_NVIC_SetPriority(DMAMUX1_OVR_IRQn, 0, 0);  HAL_NVIC_EnableIRQ(DMAMUX1_OVR_IRQn);
}

static void MX_GPIO_Init(void)
{
  GPIO_InitTypeDef GPIO_InitStruct = {0};
  __HAL_RCC_GPIOE_CLK_ENABLE();
  __HAL_RCC_GPIOC_CLK_ENABLE();
  __HAL_RCC_GPIOH_CLK_ENABLE();
  __HAL_RCC_GPIOB_CLK_ENABLE();
  __HAL_RCC_GPIOD_CLK_ENABLE();
  __HAL_RCC_GPIOA_CLK_ENABLE();
  /* ST7735: CS must be high when idle; holding CS low at boot corrupts SPI / controller state. */
  HAL_GPIO_WritePin(LCD_CS_GPIO_Port, LCD_CS_Pin, GPIO_PIN_SET);
  HAL_GPIO_WritePin(GPIOE, LED_Pin | LCD_WR_RS_Pin, GPIO_PIN_RESET);
  HAL_GPIO_WritePin(F_CS_GPIO_Port, F_CS_Pin, GPIO_PIN_RESET);
  GPIO_InitStruct.Pin = LED_Pin|LCD_CS_Pin|LCD_WR_RS_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(GPIOE, &GPIO_InitStruct);
  GPIO_InitStruct.Pin = K1_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_INPUT;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  HAL_GPIO_Init(K1_GPIO_Port, &GPIO_InitStruct);
  GPIO_InitStruct.Pin = F_CS_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(F_CS_GPIO_Port, &GPIO_InitStruct);
}

/* USER CODE BEGIN 4 */
/* USER CODE END 4 */

static void MPU_Config(void)
{
  MPU_Region_InitTypeDef MPU_InitStruct = {0};
  HAL_MPU_Disable();
  MPU_InitStruct.Enable = MPU_REGION_ENABLE;
  MPU_InitStruct.Number = MPU_REGION_NUMBER0;
  MPU_InitStruct.BaseAddress = 0x0;
  MPU_InitStruct.Size = MPU_REGION_SIZE_4GB;
  MPU_InitStruct.SubRegionDisable = 0x87;
  MPU_InitStruct.TypeExtField = MPU_TEX_LEVEL0;
  MPU_InitStruct.AccessPermission = MPU_REGION_NO_ACCESS;
  MPU_InitStruct.DisableExec = MPU_INSTRUCTION_ACCESS_DISABLE;
  MPU_InitStruct.IsShareable = MPU_ACCESS_SHAREABLE;
  MPU_InitStruct.IsCacheable = MPU_ACCESS_NOT_CACHEABLE;
  MPU_InitStruct.IsBufferable = MPU_ACCESS_NOT_BUFFERABLE;
  HAL_MPU_ConfigRegion(&MPU_InitStruct);

#if defined(APP_EXECUTE_FROM_QSPI)
  /* QSPI XIP: executable, cacheable (WeAct ExtMem / STM32H750VBTX_EXTMEM.ld). */
  MPU_InitStruct.Number = MPU_REGION_NUMBER1;
  MPU_InitStruct.BaseAddress = 0x90000000UL;
  MPU_InitStruct.Size = MPU_REGION_SIZE_8MB;
  MPU_InitStruct.SubRegionDisable = 0x00;
  MPU_InitStruct.TypeExtField = MPU_TEX_LEVEL1;
  MPU_InitStruct.AccessPermission = MPU_REGION_PRIV_RO;
  MPU_InitStruct.DisableExec = MPU_INSTRUCTION_ACCESS_ENABLE;
  MPU_InitStruct.IsShareable = MPU_ACCESS_NOT_SHAREABLE;
  MPU_InitStruct.IsCacheable = MPU_ACCESS_CACHEABLE;
  MPU_InitStruct.IsBufferable = MPU_ACCESS_BUFFERABLE;
  HAL_MPU_ConfigRegion(&MPU_InitStruct);
#else
  /* QSPI assets (splash bitmap in .qspi_rodata) — read-only, non-cacheable (no D-Cache on). */
  MPU_InitStruct.Number = MPU_REGION_NUMBER1;
  MPU_InitStruct.BaseAddress = 0x90000000UL;
  MPU_InitStruct.Size = MPU_REGION_SIZE_8MB;
  MPU_InitStruct.SubRegionDisable = 0x00;
  MPU_InitStruct.TypeExtField = MPU_TEX_LEVEL1;
  MPU_InitStruct.AccessPermission = MPU_REGION_PRIV_RO;
  MPU_InitStruct.DisableExec = MPU_INSTRUCTION_ACCESS_DISABLE;
  MPU_InitStruct.IsShareable = MPU_ACCESS_NOT_SHAREABLE;
  MPU_InitStruct.IsCacheable = MPU_ACCESS_NOT_CACHEABLE;
  MPU_InitStruct.IsBufferable = MPU_ACCESS_NOT_BUFFERABLE;
  HAL_MPU_ConfigRegion(&MPU_InitStruct);
#endif

  HAL_MPU_Enable(MPU_PRIVILEGED_DEFAULT);
}

void Error_Handler(void)
{
  __disable_irq();
  while (1) {}
}

#ifdef USE_FULL_ASSERT
void assert_failed(uint8_t *file, uint32_t line)
{
  (void)file; (void)line;
}
#endif
