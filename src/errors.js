/**
 * A message for the log. Node reports a host with several addresses, such as
 * localhost, as an AggregateError whose own message is empty.
 */
export function describeError(error) {
  return error.message || error.errors?.map(describeError).join('; ') || error.code || error.name;
}
