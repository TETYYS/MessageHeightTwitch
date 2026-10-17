//! C exports over read-fonts for the OpenType tables HarfBuzz doesn't expose

use core::ffi::c_void;
use core::slice;

use read_fonts::tables::eblc::Eblc;
use read_fonts::tables::gasp::Gasp;
use read_fonts::tables::gsub::{Gsub, SubstitutionLookup};
use read_fonts::tables::layout::ChainedSequenceContext;
use read_fonts::types::GlyphId;
use read_fonts::{FontData, FontRead, ReadError};

/// The positions of the caller's glyph list a rule position accepts, laid out as a .NET UInt128.
type Mask = [u64; 2];
const MASK_BITS: usize = 128;

/// A borrowed C array; null is allowed when `length` is 0.
unsafe fn array<'a, T>(data: *const T, length: usize) -> &'a [T] {
    if length == 0 { &[] } else { unsafe { slice::from_raw_parts(data, length) } }
}

fn matching(glyphs: &[GlyphId], accepts: impl Fn(usize, GlyphId) -> bool) -> Mask {
    let mut mask = [0; 2];
    for (index, &glyph) in glyphs.iter().enumerate() {
        if accepts(index, glyph) {
            mask[index / 64] |= 1 << (index % 64);
        }
    }
    mask
}

/// Calls `rule` for every substitution in the GSUB `lookups`, in order, with one mask per glyph
/// position of the rule: bit i is set when `glyphs[i]` can occupy that position. A ligature's
/// positions are its first glyph, one entry of `glyphs`, and its components. Returns false for
/// a malformed table or a subtable that is neither a ligature nor a chained context of format 3.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn mht_gsub_rules(
    gsub: *const u8, gsub_length: usize,
    lookups: *const u16, lookup_count: usize,
    glyphs: *const u32, glyph_count: usize,
    context: *mut c_void,
    rule: unsafe extern "C" fn(context: *mut c_void, masks: *const Mask, count: usize),
) -> bool {
    let (gsub, lookups, glyphs) = unsafe { (array(gsub, gsub_length), array(lookups, lookup_count), array(glyphs, glyph_count)) };
    let glyphs: Vec<GlyphId> = glyphs.iter().copied().map(GlyphId::new).collect();
    glyphs.len() <= MASK_BITS && gsub_rules(gsub, lookups, &glyphs, |masks| unsafe { rule(context, masks.as_ptr(), masks.len()) }).is_ok()
}

fn gsub_rules(gsub: &[u8], lookups: &[u16], glyphs: &[GlyphId], emit: impl Fn(&[Mask])) -> Result<(), ReadError> {
    let list = Gsub::read(FontData::new(gsub))?.lookup_list()?;
    for &index in lookups {
        match list.lookups().get(index.into())? {
            SubstitutionLookup::Ligature(lookup) => {
                for subtable in lookup.subtables().iter() {
                    let subtable = subtable?;
                    let coverage = subtable.coverage()?;
                    for (first, &glyph) in glyphs.iter().enumerate() {
                        let Some(set) = coverage.get(glyph) else { continue };
                        for ligature in subtable.ligature_sets().get(set.into())?.ligatures().iter() {
                            let mut masks = vec![matching(glyphs, |index, _| index == first)];
                            for component in ligature?.component_glyph_ids() {
                                masks.push(matching(glyphs, |_, g| g == GlyphId::from(component.get())));
                            }
                            emit(&masks);
                        }
                    }
                }
            }
            SubstitutionLookup::ChainContextual(lookup) => {
                for subtable in lookup.subtables().iter() {
                    let ChainedSequenceContext::Format3(context) = subtable? else {
                        return Err(ReadError::MalformedData("unsupported chained context format"));
                    };
                    // Without sequence lookup records the rule is a context exclusion, no substitution
                    if context.seq_lookup_records().is_empty() {
                        continue;
                    }
                    let coverages = context.backtrack_coverages().iter()
                        .chain(context.input_coverages().iter())
                        .chain(context.lookahead_coverages().iter());
                    let mut masks = Vec::new();
                    for coverage in coverages {
                        let coverage = coverage?;
                        masks.push(matching(glyphs, |_, g| coverage.get(g).is_some()));
                    }
                    emit(&masks);
                }
            }
            _ => return Err(ReadError::MalformedData("unsupported lookup type")),
        }
    }
    Ok(())
}

#[repr(C)]
pub struct GaspRange {
    lower: i32,
    upper: i32,
    behavior: i32,
}

/// Finds the gasp range covering `ppem`. Ranges are ordered by their upper bound, so each starts one
/// past the previous upper bound.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn mht_gasp_range(gasp: *const u8, length: usize, ppem: i32, range: *mut GaspRange) -> bool {
    let Ok(gasp) = Gasp::read(FontData::new(unsafe { array(gasp, length) })) else { return false };
    let mut lower = 0;
    for record in gasp.gasp_ranges() {
        let upper = record.range_max_ppem().into();
        if ppem <= upper {
            unsafe { range.write(GaspRange { lower, upper, behavior: record.range_gasp_behavior().bits().into() }) };
            return true;
        }
        lower = upper + 1;
    }
    false
}

#[repr(C)]
pub struct BitmapStrike {
    ppem_x: i32,
    ppem_y: i32,
    start_glyph: i32,
    end_glyph: i32,
}

/// Writes up to `capacity` of the EBLC bitmap strikes to `strikes` and returns how many there are.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn mht_bitmap_strikes(eblc: *const u8, length: usize, strikes: *mut BitmapStrike, capacity: usize) -> usize {
    let Ok(eblc) = Eblc::read(FontData::new(unsafe { array(eblc, length) })) else { return 0 };
    let sizes = eblc.bitmap_sizes();
    for (index, size) in sizes.iter().take(capacity).enumerate() {
        unsafe {
            strikes.add(index).write(BitmapStrike {
                ppem_x: size.ppem_x().into(),
                ppem_y: size.ppem_y().into(),
                start_glyph: size.start_glyph_index().to_u16().into(),
                end_glyph: size.end_glyph_index().to_u16().into(),
            })
        };
    }
    sizes.len()
}
