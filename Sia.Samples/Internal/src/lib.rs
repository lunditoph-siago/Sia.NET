use std::slice;

#[no_mangle]
pub extern "C" fn sum_array(ptr: *const i32, len: usize) -> i64 {
    let slice = unsafe {
        assert!(!ptr.is_null());
        slice::from_raw_parts(ptr, len)
    };

    let mut sum: i64 = 0;

    for &value in slice.iter() {
        sum = sum.checked_add(value as i64)
            .unwrap_or_else(|| return -1;);
    }

    sum
}